using VmManager.Backends.Shared;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services;

public class EnvironmentService
{
    public const int DefaultTtlMinutes = 1440;
    public const int MaxTtlMinutes = 4320;
    private const int FailedDebugTtlMinutes = 120;

    private static readonly SemaphoreSlim ImportGate = new SemaphoreSlim(1, 1);

    private readonly IVmBackend _backend;
    private readonly IVmIpResolver _ipResolver;
    private readonly IBackgroundTaskManager _tasks;
    private readonly SettingsService _settings;
    private readonly IVmTrackingService _vmTracking;
    private readonly ILocalImageMetadataService _localImages;
    private readonly VmOwnershipService _ownership;
    private readonly EnvironmentStore _store;
    private readonly EnvironmentAccessService _access;
    private readonly EnvironmentProvisioner _provisioner;
    private readonly QuotaService _quota;
    private readonly EmailService _email;
    private readonly UserService _users;
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(
        IVmBackend backend,
        IVmIpResolver ipResolver,
        IBackgroundTaskManager tasks,
        SettingsService settings,
        IVmTrackingService vmTracking,
        ILocalImageMetadataService localImages,
        VmOwnershipService ownership,
        EnvironmentStore store,
        EnvironmentAccessService access,
        EnvironmentProvisioner provisioner,
        QuotaService quota,
        EmailService email,
        UserService users,
        ILogger<EnvironmentService> logger
    )
    {
        _backend = backend;
        _ipResolver = ipResolver;
        _tasks = tasks;
        _settings = settings;
        _vmTracking = vmTracking;
        _localImages = localImages;
        _ownership = ownership;
        _store = store;
        _access = access;
        _provisioner = provisioner;
        _quota = quota;
        _email = email;
        _users = users;
        _logger = logger;
    }

    public async Task<List<EnvironmentView>> ListAsync()
    {
        List<EnvironmentView> views = new List<EnvironmentView>();
        foreach (EnvironmentMetadata env in _store.GetAll())
            views.Add(await ToViewAsync(env, resolveIp: false));
        return views.OrderByDescending(v => v.CreatedAt).ToList();
    }

    public async Task<EnvironmentView?> GetAsync(string key)
    {
        EnvironmentMetadata? env = _store.Get(key);
        return env == null ? null : await ToViewAsync(env, resolveIp: true);
    }

    public string? GetLogText(string key)
    {
        EnvironmentMetadata? env = _store.Get(key);
        if (env?.ProvisionLogPath == null || !File.Exists(env.ProvisionLogPath))
            return null;
        return File.ReadAllText(env.ProvisionLogPath);
    }

    public async Task<ProvisionOutcome> ProvisionAsync(
        EnvironmentProvisionRequest request,
        string caller
    )
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return new ProvisionOutcome(
                ProvisionStatus.BadRequest,
                "",
                Message: "Key is required."
            );

        request.Provision ??= new EnvironmentProvisionSpec();
        string owner = string.IsNullOrWhiteSpace(request.Owner) ? caller : request.Owner.Trim();

        EnvironmentMetadata? existing = _store.Get(request.Key);
        if (existing != null)
        {
            switch (request.IfExists)
            {
                case EnvironmentExistsBehavior.Reuse:
                    return new ProvisionOutcome(ProvisionStatus.Reused, existing.Key);
                case EnvironmentExistsBehavior.Fail:
                    return new ProvisionOutcome(
                        ProvisionStatus.Conflict,
                        request.Key,
                        Message: $"Environment '{request.Key}' already exists."
                    );
            }
        }

        if (existing == null)
        {
            QuotaCheckResult quota = await _quota.CheckCanCreateVmAsync(owner);
            if (!quota.Allowed)
                return new ProvisionOutcome(
                    ProvisionStatus.BadRequest,
                    request.Key,
                    Message: quota.Reason
                );
        }

        AppSettings settings = _settings.Load();
        string vmName = SanitizeVmName(request.Key);

        string? extractedFolder = ResolveExtractedFolder(settings, request.Image, request.Version);
        if (extractedFolder == null)
            return new ProvisionOutcome(
                ProvisionStatus.BadRequest,
                request.Key,
                Message: $"No locally extracted image matches '{request.Image}'"
                    + (string.IsNullOrEmpty(request.Version) ? "" : $" version '{request.Version}'")
                    + ". Import it first."
            );

        int ttl =
            request.TtlMinutes <= 0
                ? EffectiveDefaultTtl(settings)
                : Math.Min(request.TtlMinutes, EffectiveMaxTtl(settings));

        EnvironmentMetadata env =
            existing
            ?? new EnvironmentMetadata
            {
                Key = request.Key,
                VmName = vmName,
                CreatedAt = DateTime.UtcNow,
            };
        env.VmName = vmName;
        env.Owner = owner;
        env.Labels = request.Labels ?? new Dictionary<string, string>();
        env.AccessEmails = request.AccessEmails ?? [];
        env.Status = EnvironmentStatus.Provisioning;
        env.LastError = null;
        _store.Upsert(env);

        IBackgroundTask task = _tasks.StartTask(
            TaskTitle(request.Key),
            owner,
            ct => ProvisionWorkAsync(ct, env, request, settings, extractedFolder, ttl)
        );

        return new ProvisionOutcome(ProvisionStatus.Accepted, request.Key, task.Id);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        EnvironmentMetadata? env = _store.Get(key);
        if (env == null)
            return false;
        await TeardownAsync(env);
        return true;
    }

    public DateTime? Extend(string key, int minutes)
    {
        EnvironmentMetadata? env = _store.Get(key);
        if (env == null)
            return null;

        DateTime from = env.ExpiresAt is { } e && e > DateTime.UtcNow ? e : DateTime.UtcNow;
        DateTime maxAllowed = DateTime.UtcNow.AddMinutes(EffectiveMaxTtl(_settings.Load()));
        DateTime candidate = from.AddMinutes(Math.Max(1, minutes));
        env.ExpiresAt = candidate > maxAllowed ? maxAllowed : candidate;
        if (env.Status == EnvironmentStatus.Expiring)
            env.Status = EnvironmentStatus.Ready;
        _store.Upsert(env);
        return env.ExpiresAt;
    }

    public static string TaskTitle(string key) => $"Provisioning environment {key}";

    private static int EffectiveDefaultTtl(AppSettings s) =>
        s.DefaultEnvTtlMinutes > 0 ? s.DefaultEnvTtlMinutes : DefaultTtlMinutes;

    private static int EffectiveMaxTtl(AppSettings s) =>
        s.MaxEnvTtlMinutes > 0 ? s.MaxEnvTtlMinutes : MaxTtlMinutes;

    public async Task<int> CleanupAsync(int warnLeadMinutes)
    {
        DateTime now = DateTime.UtcNow;
        int deleted = 0;

        HashSet<string>? liveVmNames = await TryGetLiveVmNamesAsync();

        foreach (EnvironmentMetadata env in _store.GetAll())
        {
            if (
                liveVmNames != null
                && env.Status != EnvironmentStatus.Provisioning
                && !liveVmNames.Contains(env.VmName)
            )
            {
                _logger.LogInformation("Reconciling orphaned environment {Key} (VM gone)", env.Key);
                _access.RevokeAccess(env.VmName);
                _ownership.RemoveOwner(env.VmName);
                _store.Remove(env.Key);
                continue;
            }

            if (env.ExpiresAt is not { } expiresAt)
                continue;

            if (now >= expiresAt)
            {
                _logger.LogInformation("Environment {Key} expired; deleting", env.Key);
                await TeardownAsync(env);
                deleted++;
                _ = SendOwnerMailAsync(
                    env,
                    $"Test environment '{env.Key}' was removed",
                    $"<p>Environment <b>{env.Key}</b> reached its expiry and has been deleted.</p>"
                );
            }
            else if (
                env.Status == EnvironmentStatus.Ready
                && now >= expiresAt.AddMinutes(-Math.Max(1, warnLeadMinutes))
            )
            {
                env.Status = EnvironmentStatus.Expiring;
                _store.Upsert(env);
                _ = SendOwnerMailAsync(
                    env,
                    $"Test environment '{env.Key}' expires soon",
                    $"<p>Environment <b>{env.Key}</b> expires at {expiresAt:u}. Extend it from the Test Environments page if you still need it.</p>"
                );
            }
        }

        return deleted;
    }

    private async Task<HashSet<string>?> TryGetLiveVmNamesAsync()
    {
        try
        {
            List<VmInstance> vms = await _backend.GetVmsAsync();
            return vms.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list VMs for orphan reconcile; skipping this pass");
            return null;
        }
    }

    private async Task ProvisionWorkAsync(
        BackgroundTaskContext ctx,
        EnvironmentMetadata env,
        EnvironmentProvisionRequest request,
        AppSettings settings,
        string extractedFolder,
        int ttl
    )
    {
        string vmName = env.VmName;
        try
        {
            if (request.IfExists == EnvironmentExistsBehavior.Replace)
            {
                ctx.ReportProgress(0.02, "Removing previous environment...");
                await TryDeleteVmAsync(vmName);
                _access.RevokeAccess(vmName);
            }

            ctx.ReportProgress(0.05, "Importing VM...");
            await ImportGate.WaitAsync(ctx.Token);
            try
            {
                await _backend.ImportVmAsync(
                    extractedFolder,
                    settings.LocalVmPath,
                    request.MemoryMb,
                    request.CpuCount,
                    vmName,
                    skipDefaultNetwork: false,
                    onStatus: s => ctx.ReportProgress(-1, s),
                    cancellationToken: ctx.Token
                );
            }
            finally
            {
                ImportGate.Release();
            }
            _vmTracking.TrackVm(vmName, null);
            _ownership.SetOwner(vmName, env.Owner);

            ctx.ReportProgress(0.3, "Starting VM...");
            await _backend.StartVmAsync(vmName);

            ctx.ReportProgress(0.35, "Waiting for VM IP...");
            string ip = await WaitForIpAsync(vmName, TimeSpan.FromMinutes(5), ctx.Token);

            ctx.ReportProgress(0.5, "Waiting for WinRM...");
            await WinRmLocaleHelper.WaitForWinRmAsync(ip, TimeSpan.FromMinutes(3));

            ctx.ReportProgress(0.6, "Running provisioning...");
            ProvisionResult result = await _provisioner.RunAsync(
                ip,
                settings.DefaultVmUsername,
                settings.DefaultVmPassword,
                request.Provision,
                ctx.Log,
                ctx.Token
            );
            env.ProvisionLogPath = WriteProvisionLog(settings, env.Key, result.Output);

            if (!result.Success)
            {
                await HandleFailureAsync(
                    ctx,
                    env,
                    request,
                    $"Provisioning failed (exit {result.ExitCode}). See provision log."
                );
                throw new InvalidOperationException(env.LastError);
            }

            ctx.ReportProgress(0.9, "Creating ready snapshot...");
            await TryCreateSnapshotAsync(vmName, "ready");

            ctx.ReportProgress(0.94, "Granting access...");
            await _access.GrantAccessAsync(vmName, env.Owner, env.AccessEmails);

            env.Status = EnvironmentStatus.Ready;
            env.ExpiresAt = DateTime.UtcNow.AddMinutes(ttl);
            env.LastError = null;
            _store.Upsert(env);

            ctx.ReportProgress(1.0, "Ready");
            _ = SendOwnerMailAsync(env, $"Test environment '{env.Key}' is ready", ReadyBody(env));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
            when (ex is not InvalidOperationException || env.Status != EnvironmentStatus.Failed)
        {
            _logger.LogError(ex, "Environment provisioning failed for {Key}", env.Key);
            await HandleFailureAsync(ctx, env, request, ex.Message);
            throw;
        }
    }

    private async Task HandleFailureAsync(
        BackgroundTaskContext ctx,
        EnvironmentMetadata env,
        EnvironmentProvisionRequest request,
        string error
    )
    {
        env.Status = EnvironmentStatus.Failed;
        env.LastError = error;

        if (request.Provision.OnFailure == ProvisionFailureBehavior.Destroy)
        {
            ctx.Log("OnFailure=Destroy: tearing down environment.");
            await TryDeleteVmAsync(env.VmName);
            _access.RevokeAccess(env.VmName);
            _store.Remove(env.Key);
        }
        else
        {
            env.ExpiresAt = DateTime.UtcNow.AddMinutes(FailedDebugTtlMinutes);
            _store.Upsert(env);
        }

        _ = SendOwnerMailAsync(
            env,
            $"Test environment '{env.Key}' failed",
            $"<h2>Provisioning failed</h2><p>{System.Net.WebUtility.HtmlEncode(error)}</p>"
        );
    }

    private async Task TeardownAsync(EnvironmentMetadata env)
    {
        await TryDeleteVmAsync(env.VmName);
        _access.RevokeAccess(env.VmName);
        _vmTracking.UntrackVm(env.VmName);
        _ownership.RemoveOwner(env.VmName);
        _store.Remove(env.Key);
    }

    private async Task<string> WaitForIpAsync(string vmName, TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string? ip = await _ipResolver.ResolveIpAsync(vmName, ct);
            if (!string.IsNullOrWhiteSpace(ip))
                return ip;
            await Task.Delay(5000, ct);
        }
        throw new TimeoutException(
            $"VM {vmName} did not report an IP within {timeout.TotalSeconds:0}s"
        );
    }

    private async Task TryDeleteVmAsync(string vmName)
    {
        try
        {
            await _backend.DeleteVmAsync(vmName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete of VM {VmName} failed (may not exist)", vmName);
        }
    }

    private async Task TryCreateSnapshotAsync(string vmName, string name)
    {
        try
        {
            await _backend.CreateSnapshotAsync(vmName, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot '{Name}' for {VmName} failed", name, vmName);
        }
    }

    private static string WriteProvisionLog(AppSettings settings, string key, string content)
    {
        string dir = Path.Combine(settings.LocalVmPath, "environment-logs");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, SanitizeVmName(key) + ".log");
        File.WriteAllText(path, content ?? "");
        return path;
    }

    private string? ResolveExtractedFolder(AppSettings settings, string image, string version)
    {
        string root = Path.Combine(settings.LocalVmPath, "extracted");
        if (!Directory.Exists(root))
            return null;

        bool matchAny = string.IsNullOrWhiteSpace(image);
        foreach (string dir in Directory.GetDirectories(root))
        {
            LocalImageMetadata? meta = _localImages.LoadMetadata(dir);
            bool imageMatch =
                matchAny
                || Matches(meta?.ParentImageId, image)
                || Matches(meta?.ParentImageName, image)
                || string.Equals(Path.GetFileName(dir), image, StringComparison.OrdinalIgnoreCase);
            bool versionMatch =
                string.IsNullOrWhiteSpace(version) || Matches(meta?.Version, version);
            if (imageMatch && versionMatch)
                return dir;
        }
        return null;
    }

    private static bool Matches(string? a, string b) =>
        a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private async Task<EnvironmentView> ToViewAsync(EnvironmentMetadata env, bool resolveIp)
    {
        AppSettings settings = _settings.Load();
        string? rdpTarget = null;
        if (!string.IsNullOrWhiteSpace(settings.RdpDomainSuffix))
            rdpTarget = $"{env.VmName}.{settings.RdpDomainSuffix.TrimStart('.')}";
        else if (resolveIp && env.Status == EnvironmentStatus.Ready)
            rdpTarget = await _ipResolver.ResolveIpAsync(env.VmName);

        string? taskId = _tasks
            .Tasks.FirstOrDefault(t =>
                t.Title == TaskTitle(env.Key) && !t.IsComplete && !t.IsFailed && !t.IsCancelled
            )
            ?.Id;

        return new EnvironmentView
        {
            Key = env.Key,
            VmName = env.VmName,
            Owner = env.Owner,
            Labels = env.Labels,
            Status = env.Status,
            CreatedAt = env.CreatedAt,
            ExpiresAt = env.ExpiresAt,
            LastError = env.LastError,
            AccessEmails = env.AccessEmails,
            RdpTarget = rdpTarget,
            TaskId = taskId,
        };
    }

    private async Task SendOwnerMailAsync(EnvironmentMetadata env, string subject, string body)
    {
        try
        {
            UserAccount? user = _users.GetByUsername(env.Owner);
            string? to =
                user == null ? null
                : user.IsAdmin ? user.Email
                : EmailValidator.IsValid(user.Username) ? user.Username
                : user.Email;
            if (string.IsNullOrWhiteSpace(to))
                return;
            await _email.SendAsync(to, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send owner mail for {Key}", env.Key);
        }
    }

    private static string ReadyBody(EnvironmentMetadata env)
    {
        string labels = string.Join(
            "",
            env.Labels.Select(kv =>
                $"<li><b>{kv.Key}:</b> {System.Net.WebUtility.HtmlEncode(kv.Value)}</li>"
            )
        );
        return $@"<h2>Test environment ready</h2>
<p>Environment <b>{env.Key}</b> (VM <b>{env.VmName}</b>) is ready to use.</p>
<ul>{labels}</ul>
<p>Connect via the VmManager client; it expires at {env.ExpiresAt:u}.</p>";
    }

    public static string SanitizeVmName(string key)
    {
        char[] chars = key.Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')
            .ToArray();
        string name = new string(chars).Trim('-');
        if (name.Length > 60)
            name = name[..60].Trim('-');
        return string.IsNullOrEmpty(name) ? "env" : name;
    }
}

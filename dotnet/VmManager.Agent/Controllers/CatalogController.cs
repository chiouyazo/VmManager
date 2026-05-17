using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VmManager.Agent.Services;

namespace VmManager.Agent.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CatalogController : ControllerBase
{
    private readonly CatalogAggregator _catalogAggregator;
    private readonly ImportService _importService;
    private readonly IVmBackend _backend;
    private readonly INetworkService _networkService;
    private readonly NetworkProvisioningService _networkProvisioningService;
    private readonly NetworkTrackingService _networkTrackingService;
    private readonly SettingsService _settingsService;
    private readonly IVmTrackingService _vmTrackingService;
    private readonly ILocalImageMetadataService _localImageMetadataService;
    private readonly IBackgroundTaskManager _backgroundTaskManager;
    private readonly VmOwnershipService _ownershipService;
    private readonly ILogger<CatalogController> _logger;

    public CatalogController(
        CatalogAggregator catalogAggregator,
        ImportService importService,
        IVmBackend backend,
        INetworkService networkService,
        NetworkProvisioningService networkProvisioningService,
        NetworkTrackingService networkTrackingService,
        SettingsService settingsService,
        IVmTrackingService vmTrackingService,
        ILocalImageMetadataService localImageMetadataService,
        IBackgroundTaskManager backgroundTaskManager,
        VmOwnershipService ownershipService,
        ILogger<CatalogController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(catalogAggregator);
        ArgumentNullException.ThrowIfNull(importService);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(networkService);
        ArgumentNullException.ThrowIfNull(networkProvisioningService);
        ArgumentNullException.ThrowIfNull(networkTrackingService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(vmTrackingService);
        ArgumentNullException.ThrowIfNull(localImageMetadataService);
        ArgumentNullException.ThrowIfNull(backgroundTaskManager);
        ArgumentNullException.ThrowIfNull(ownershipService);
        ArgumentNullException.ThrowIfNull(logger);
        _catalogAggregator = catalogAggregator;
        _importService = importService;
        _backend = backend;
        _networkService = networkService;
        _networkProvisioningService = networkProvisioningService;
        _networkTrackingService = networkTrackingService;
        _settingsService = settingsService;
        _vmTrackingService = vmTrackingService;
        _localImageMetadataService = localImageMetadataService;
        _backgroundTaskManager = backgroundTaskManager;
        _ownershipService = ownershipService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = Permission.CatalogBrowse)]
    [ProducesResponseType(typeof(List<VmImage>), 200)]
    public async Task<IActionResult> LoadCatalog(CancellationToken cancellationToken)
    {
        List<VmImage> images = await _catalogAggregator.LoadCatalogAsync();

        AppSettings settings = _settingsService.Load();
        string extractedRoot = Path.Combine(settings.LocalVmPath, "extracted");
        if (!Directory.Exists(extractedRoot))
            return Ok(images);

        Dictionary<string, string> localImagesByKey = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (string dir in Directory.GetDirectories(extractedRoot))
        {
            LocalImageMetadata? meta = _localImageMetadataService.LoadMetadata(dir);
            if (meta == null)
                continue;

            string key =
                (meta.FeedId ?? "") + "|" + (meta.ParentImageId ?? "") + "|" + (meta.Version ?? "");
            localImagesByKey[key] = dir;
        }

        foreach (VmImage img in images)
        {
            foreach (VmImageVersion ver in img.Versions)
            {
                string key =
                    (ver.FeedId ?? "")
                    + "|"
                    + (ver.ParentImageId ?? "")
                    + "|"
                    + (ver.Version ?? "");
                if (localImagesByKey.ContainsKey(key))
                    ver.IsLocallyAvailable = true;
            }
        }

        return Ok(images);
    }

    [HttpGet("local")]
    [Authorize(Policy = Permission.CatalogBrowse)]
    [ProducesResponseType(typeof(List<LocalImage>), 200)]
    public IActionResult GetLocalImages()
    {
        AppSettings settings = _settingsService.Load();
        string extractedDir = Path.Combine(settings.LocalVmPath, "extracted");

        List<LocalImage> locals = new List<LocalImage>();
        if (!Directory.Exists(extractedDir))
            return Ok(locals);

        foreach (string dir in Directory.GetDirectories(extractedDir))
        {
            string[] diskFiles = Directory
                .GetFiles(dir, "*.vhdx", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(dir, "*.qcow2", SearchOption.AllDirectories))
                .ToArray();
            if (diskFiles.Length == 0)
                continue;

            DirectoryInfo dirInfo = new DirectoryInfo(dir);
            long totalSize = diskFiles.Sum(f => new FileInfo(f).Length);

            string displayName = dirInfo.Name;
            string? feedId = null,
                feedUrl = null,
                feedRepo = null;
            string? parentImageId = null,
                parentImageName = null,
                imageVersion = null;

            LocalImageMetadata? meta = _localImageMetadataService.LoadMetadata(dir);
            if (meta != null)
            {
                if (!string.IsNullOrEmpty(meta.Name))
                    displayName = meta.Name;
                feedId = meta.FeedId;
                feedUrl = meta.FeedUrl;
                feedRepo = meta.FeedRepository;
                parentImageId = meta.ParentImageId;
                parentImageName = meta.ParentImageName;
                imageVersion = meta.Version;
            }

            locals.Add(
                new LocalImage
                {
                    Name = displayName,
                    Path = dir,
                    SizeGb = totalSize / 1073741824.0,
                    ExtractedAt = dirInfo.CreationTime,
                    FeedId = feedId,
                    FeedUrl = feedUrl,
                    FeedRepository = feedRepo,
                    ParentImageId = parentImageId,
                    ParentImageName = parentImageName,
                    ImageVersion = imageVersion,
                }
            );
        }

        return Ok(locals.OrderByDescending(l => l.ExtractedAt));
    }

    [HttpDelete("local")]
    [Authorize(Policy = Permission.CatalogDeleteLocal)]
    [ProducesResponseType(204)]
    public IActionResult DeleteLocalImage([FromQuery] string path)
    {
        if (Directory.Exists(path))
        {
            string dirName = Path.GetFileName(path);
            Directory.Delete(path, true);

            AppSettings settings = _settingsService.Load();
            string downloadsDir = Path.Combine(settings.LocalVmPath, "downloads");
            if (Directory.Exists(downloadsDir))
            {
                foreach (string file in Directory.GetFiles(downloadsDir))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(dirName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Deleting download file: {File}", file);
                        System.IO.File.Delete(file);
                    }
                }
            }
        }
        return NoContent();
    }

    [HttpPost("import")]
    [Authorize(Policy = Permission.CatalogImport)]
    [ProducesResponseType(typeof(object), 202)]
    public IActionResult ImportVersion([FromBody] ImportRequest request)
    {
        _logger.LogInformation("Starting import for {VersionRef}", request.VersionRef);

        AppSettings settings = _settingsService.Load();

        IBackgroundTask task = _backgroundTaskManager.StartTask(
            "Importing " + request.VersionRef,
            async ctx =>
            {
                string downloadUrl = await _catalogAggregator.GetDownloadUrlAsync(
                    request.VersionRef
                );

                System.Net.Http.Headers.AuthenticationHeaderValue? auth = null;
                if (CatalogAggregator.IsNexusVersion(request.VersionRef))
                    auth = _catalogAggregator.GetNexusAuthHeader();
                else if (!CatalogAggregator.IsLocalVersion(request.VersionRef))
                    auth = _catalogAggregator.GetAuthHeader();

                string downloadPath = Path.Combine(
                    settings.LocalVmPath,
                    "downloads",
                    request.SafeFileName + ".box"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);

                ctx.ReportProgress(0, "Downloading...");
                await _importService.DownloadWithProgressAsync(
                    downloadUrl,
                    downloadPath,
                    new Progress<DownloadProgress>(p =>
                    {
                        double speedMb = p.SpeedBytesPerSec / 1024.0 / 1024.0;
                        double dlMb = p.DownloadedBytes / 1024.0 / 1024.0;
                        double totalMb = p.TotalBytes / 1024.0 / 1024.0;
                        string status = p.Eta.HasValue
                            ? string.Format(
                                "Downloading: {0:F0}/{1:F0} MB ({2:F1} MB/s, ~{3:mm\\:ss} remaining)",
                                dlMb,
                                totalMb,
                                speedMb,
                                p.Eta.Value
                            )
                            : string.Format("Downloading: {0:F0} MB ({1:F1} MB/s)", dlMb, speedMb);
                        ctx.ReportProgress(p.Percent / 200.0, status);
                    }),
                    ctx.Token,
                    auth
                );

                string extractPath = Path.Combine(
                    settings.LocalVmPath,
                    "extracted",
                    request.SafeFileName
                );
                ctx.ReportProgress(0.5, "Extracting...");
                await _importService.ExtractAsync(
                    downloadPath,
                    extractPath,
                    new Progress<double>(pct =>
                    {
                        ctx.ReportProgress(
                            0.5 + pct / 2.0,
                            string.Format("Extracting: {0:F0}%", pct)
                        );
                    }),
                    ctx.Token
                );

                if (request.Version != null)
                    _localImageMetadataService.SaveMetadata(extractPath, request.Version);

                // On Linux: convert VHDX to QCOW2 in-place and delete the VHDX
                if (OperatingSystem.IsLinux())
                {
                    string? vhdxFile = Directory
                        .GetFiles(extractPath, "*.vhdx", SearchOption.AllDirectories)
                        .Concat(
                            Directory.GetFiles(extractPath, "*.avhdx", SearchOption.AllDirectories)
                        )
                        .FirstOrDefault();

                    bool hasQcow2 = Directory
                        .GetFiles(extractPath, "*.qcow2", SearchOption.AllDirectories)
                        .Any();

                    if (vhdxFile != null && !hasQcow2)
                    {
                        string qcow2File = Path.ChangeExtension(vhdxFile, ".qcow2");
                        long vhdxSize = new FileInfo(vhdxFile).Length;

                        ctx.ReportProgress(0.9, "Converting VHDX to QCOW2...");
                        _logger.LogInformation(
                            "Converting VHDX in-place: {Source} -> {Dest}",
                            vhdxFile,
                            qcow2File
                        );

                        Task convertTask = Task.Run(async () =>
                        {
                            Process proc = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "qemu-img",
                                    Arguments = $"convert -O qcow2 \"{vhdxFile}\" \"{qcow2File}\"",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                },
                            };
                            proc.Start();
                            await proc.WaitForExitAsync(ctx.Token);
                            if (proc.ExitCode != 0)
                            {
                                string stderr = await proc.StandardError.ReadToEndAsync();
                                throw new InvalidOperationException(
                                    $"qemu-img convert failed: {stderr}"
                                );
                            }
                        });

                        while (!convertTask.IsCompleted)
                        {
                            await Task.Delay(2000, ctx.Token);
                            if (System.IO.File.Exists(qcow2File))
                            {
                                long currentSize = new FileInfo(qcow2File).Length;
                                double pct = Math.Min((double)currentSize / vhdxSize * 100, 99);
                                ctx.ReportProgress(0.9 + pct / 1000.0, $"Converting: {pct:F0}%");
                            }
                        }

                        await convertTask; // propagate exceptions

                        System.IO.File.Delete(vhdxFile);
                        _logger.LogInformation(
                            "VHDX converted and deleted. QCOW2: {Path}",
                            qcow2File
                        );
                    }
                }

                ctx.ReportProgress(1.0, "Complete");
            }
        );

        return Accepted(new { taskId = task.Id, title = task.Title });
    }

    [HttpPost("create-vm")]
    [Authorize(Policy = Permission.VmCreate)]
    [ProducesResponseType(typeof(object), 202)]
    public IActionResult CreateVm([FromBody] CreateVmRequest request)
    {
        string currentUser = User.Identity?.Name ?? "admin";

        _logger.LogInformation(
            "Creating VM {VmName} from {ExtractedFolder}, Origin: FeedId={FeedId}, FeedUrl={FeedUrl}, Repo={Repo}, ImageId={ImageId}",
            request.Name,
            request.ExtractedFolder,
            request.Origin?.FeedId ?? "(null)",
            request.Origin?.FeedUrl ?? "(null)",
            request.Origin?.Repository ?? "(null)",
            request.Origin?.ImageId ?? "(null)"
        );

        AppSettings settings = _settingsService.Load();

        IBackgroundTask task = _backgroundTaskManager.StartTask(
            "Creating VM " + request.Name,
            async ctx =>
            {
                List<(string SwitchName, VmNetworkAdapter Config)>? networkMappings = null;
                if (request.Networks != null && request.Networks.Count > 0)
                {
                    ctx.ReportProgress(-1, "Provisioning networks...");
                    networkMappings = await _networkProvisioningService.EnsureNetworksAsync(
                        request.Networks,
                        request.Name,
                        request.Origin?.FeedId
                    );
                }

                try
                {
                    ctx.ReportProgress(-1, "Importing VM...");
                    await _backend.ImportVmAsync(
                        request.ExtractedFolder,
                        settings.LocalVmPath,
                        request.MemoryMb,
                        request.CpuCount,
                        request.Name,
                        skipDefaultNetwork: networkMappings != null && networkMappings.Count > 0,
                        onStatus: status => ctx.ReportProgress(-1, status),
                        cancellationToken: ctx.Token
                    );
                }
                catch
                {
                    if (networkMappings != null)
                        _networkTrackingService.DecrementReferences(request.Name);
                    throw;
                }

                _vmTrackingService.TrackVm(request.Name, request.Origin);
                _ownershipService.SetOwner(request.Name, currentUser);

                if (networkMappings != null && networkMappings.Count > 0)
                {
                    ctx.ReportProgress(-1, "Configuring network adapters...");
                    await _networkService.ConfigureVmAdaptersAsync(request.Name, networkMappings);
                }

                if (
                    settings.ApplyLocaleOnCreate
                    && !string.IsNullOrWhiteSpace(settings.DefaultLocale)
                    && !string.IsNullOrWhiteSpace(settings.DefaultVmUsername)
                    && !string.IsNullOrWhiteSpace(settings.DefaultVmPassword)
                )
                {
                    try
                    {
                        ctx.ReportProgress(-1, "Applying locale: " + settings.DefaultLocale);
                        ctx.Log(
                            "Applying locale: "
                                + settings.DefaultLocale
                                + ", keyboard: "
                                + settings.DefaultKeyboardLayout
                                + ", timezone: "
                                + settings.DefaultTimezone
                        );
                        await _backend.ConfigureLocaleAsync(
                            request.Name,
                            settings.DefaultVmUsername,
                            settings.DefaultVmPassword,
                            settings.DefaultLocale,
                            settings.DefaultKeyboardLayout,
                            settings.DefaultTimezone,
                            onStatus: status => ctx.ReportProgress(-1, status)
                        );
                        ctx.Log("Locale applied successfully");
                    }
                    catch (Exception localeEx)
                    {
                        _logger.LogError(
                            localeEx,
                            "Locale application failed for VM {VmName}",
                            request.Name
                        );
                        ctx.ReportProgress(-1, "Locale failed: " + localeEx.Message);
                        ctx.Log("Locale failed: " + localeEx.Message);
                    }
                }

                bool needsPostCreation =
                    settings.RenameComputerToVmName
                    || !string.IsNullOrWhiteSpace(settings.PostCreationScript);
                if (
                    needsPostCreation
                    && !string.IsNullOrWhiteSpace(settings.DefaultVmUsername)
                    && !string.IsNullOrWhiteSpace(settings.DefaultVmPassword)
                )
                {
                    try
                    {
                        await _backend.RunPostCreationAsync(
                            request.Name,
                            settings.DefaultVmUsername,
                            settings.DefaultVmPassword,
                            settings.RenameComputerToVmName,
                            settings.PostCreationScript,
                            onStatus: status => ctx.ReportProgress(-1, status)
                        );
                    }
                    catch (Exception postEx)
                    {
                        _logger.LogError(
                            postEx,
                            "Post-creation tasks failed for VM {VmName}",
                            request.Name
                        );
                        ctx.Log("Post-creation tasks failed: " + postEx.Message);
                    }
                }

                List<VmNetworkAdapter> staticIpAdapters =
                    request.Networks?.Where(a => !string.IsNullOrEmpty(a.StaticIp)).ToList() ?? [];
                if (staticIpAdapters.Count > 0)
                {
                    ctx.ReportProgress(-1, "Configuring network addresses...");
                    await _networkService.ConfigureGuestIpAsync(
                        request.Name,
                        settings.DefaultVmUsername,
                        settings.DefaultVmPassword,
                        staticIpAdapters
                    );
                }

                ctx.ReportProgress(-1, "Creating base snapshot...");
                await _backend.CreateSnapshotAsync(request.Name, "Base");

                ctx.ReportProgress(1.0, "Complete");
            }
        );

        return Accepted(new { taskId = task.Id, title = task.Title });
    }
}

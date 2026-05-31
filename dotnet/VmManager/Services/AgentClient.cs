using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using VmManager.Models;

namespace VmManager.Services;

public sealed class AgentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _username;
    private readonly string? _password;
    private readonly ILogger<AgentClient> _logger;
    private HubConnection? _hubConnection;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    public string? RdpProxyHost { get; set; }

    public AgentClient(
        string baseUrl,
        ILogger<AgentClient> logger,
        string? username = null,
        string? password = null,
        string? rdpProxyHost = null
    )
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(logger);
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username;
        _password = password;
        RdpProxyHost = rdpProxyHost;
        _logger = logger;

        HttpClientHandler handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        _http = new HttpClient(handler) { BaseAddress = new Uri(_baseUrl) };

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            string credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes(username + ":" + password)
            );
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public string BaseUrl => _baseUrl;

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(2)
            );
            HttpResponseMessage response = await _http.GetAsync("/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetBackendTypeAsync()
    {
        try
        {
            AgentStatusResponse status = await GetJsonAsync<AgentStatusResponse>("/api/status");
            return status.Backend ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task<List<AgentTaskInfo>> GetTasksAsync()
    {
        return await GetJsonAsync<List<AgentTaskInfo>>("/api/status/tasks");
    }

    public async Task CancelTaskAsync(string taskId)
    {
        await PostAsync("/api/status/tasks/" + Uri.EscapeDataString(taskId) + "/cancel");
    }

    public async Task<int> GetActiveRdpSessionCountAsync()
    {
        ActiveSessionCountResponse result = await GetJsonAsync<ActiveSessionCountResponse>(
            "/api/status/rdp-sessions/active-count"
        );
        return result.Count;
    }

    public async Task<string> TroubleshootAsync()
    {
        TroubleshootResponse result = await GetJsonAsync<TroubleshootResponse>(
            "/api/status/troubleshoot"
        );
        return result.Report;
    }

    public async Task<List<VmInstance>> GetVmsAsync()
    {
        return await GetJsonAsync<List<VmInstance>>("/api/vms");
    }

    public async Task StartVmAsync(string name)
    {
        await PostAsync("/api/vms/" + Uri.EscapeDataString(name) + "/start");
    }

    public async Task StopVmAsync(string name)
    {
        await PostAsync("/api/vms/" + Uri.EscapeDataString(name) + "/stop");
    }

    public async Task DeleteVmAsync(string name)
    {
        await DeleteAsync("/api/vms/" + Uri.EscapeDataString(name));
    }

    public async Task<bool> IsRdpReadyAsync(string name)
    {
        Dictionary<string, object>? result = await GetJsonAsync<Dictionary<string, object>>(
            "/api/vms/" + Uri.EscapeDataString(name) + "/rdp-ready"
        );
        return result?.TryGetValue("ready", out object? val) == true
            && val is JsonElement el
            && el.GetBoolean();
    }

    public async Task RenameVmAsync(string name, string newName)
    {
        await PutAsync("/api/vms/" + Uri.EscapeDataString(name) + "/rename", new { newName });
    }

    public async Task ResetVmAsync(string name)
    {
        await PostAsync("/api/vms/" + Uri.EscapeDataString(name) + "/reset");
    }

    public async Task SaveNotesAsync(string name, string notes)
    {
        await PutAsync("/api/vms/" + Uri.EscapeDataString(name) + "/notes", new { notes });
    }

    public Task ConnectToVmAsync(string name, string? rdpDomainSuffix = null)
    {
        string rdpAddress;
        string rdpUsername;

        if (!string.IsNullOrEmpty(rdpDomainSuffix))
        {
            // DNS wildcard mode: vmName.lab.domain
            string suffix = rdpDomainSuffix.TrimStart('.');
            rdpAddress = name + "." + suffix;
            rdpUsername = _username ?? "";
        }
        else
        {
            // Username-prefix mode: connect to agent, vmName:username
            Uri baseUri = new Uri(_baseUrl);
            string host = baseUri.Host;
            int port = 13389;

            if (!string.IsNullOrEmpty(RdpProxyHost))
            {
                Uri proxyUri = new Uri(
                    RdpProxyHost.Contains("://") ? RdpProxyHost : "tcp://" + RdpProxyHost
                );
                host = proxyUri.Host;
                if (proxyUri.Port > 0 && proxyUri.Port != 80)
                    port = proxyUri.Port;
            }

            rdpAddress = host + ":" + port;
            rdpUsername = name + ":" + (_username ?? "");
        }

        LaunchRdpClient(rdpAddress, rdpUsername);
        return Task.CompletedTask;
    }

    private static void LaunchRdpClient(string address, string username)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(
                new ProcessStartInfo("mstsc.exe", "/v:" + address) { UseShellExecute = true }
            );
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            string encodedAddress = Uri.EscapeDataString(address);
            string encodedUsername = Uri.EscapeDataString(username);
            string rdpUri =
                "rdp://full%20address=s:" + encodedAddress + "&username=s:" + encodedUsername;
            Process.Start("open", new[] { rdpUri });
            return;
        }

        // Linux: try xfreerdp
        Process.Start(
            new ProcessStartInfo("xfreerdp", "/v:" + address + " /u:" + username + " /cert:ignore")
            {
                UseShellExecute = true,
            }
        );
    }

    private static string? FindMacOsRdpApp()
    {
        string[] candidates =
        {
            "/Applications/Windows App.app",
            "/Applications/Microsoft Remote Desktop.app",
        };
        foreach (string path in candidates)
        {
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }

    public static bool IsRdpAppAvailable()
    {
        if (OperatingSystem.IsWindows())
            return true;
        if (OperatingSystem.IsMacOS())
            return FindMacOsRdpApp() != null;
        return false;
    }

    public async Task<List<VmSnapshot>> GetSnapshotsAsync(string vmName)
    {
        return await GetJsonAsync<List<VmSnapshot>>(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/snapshots"
        );
    }

    public async Task CreateSnapshotAsync(string vmName, string snapshotName)
    {
        await PostAsync(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/snapshots",
            new { name = snapshotName }
        );
    }

    public async Task RestoreSnapshotAsync(string vmName, string snapshotId)
    {
        await PostAsync(
            "/api/vms/"
                + Uri.EscapeDataString(vmName)
                + "/snapshots/"
                + Uri.EscapeDataString(snapshotId)
                + "/restore"
        );
    }

    public async Task DeleteSnapshotAsync(string vmName, string snapshotId)
    {
        await DeleteAsync(
            "/api/vms/"
                + Uri.EscapeDataString(vmName)
                + "/snapshots/"
                + Uri.EscapeDataString(snapshotId)
        );
    }

    public async Task CloneFromSnapshotAsync(string vmName, string snapshotId, string newName)
    {
        await PostAsync(
            "/api/vms/"
                + Uri.EscapeDataString(vmName)
                + "/snapshots/"
                + Uri.EscapeDataString(snapshotId)
                + "/clone",
            new { newName }
        );
    }

    public async Task PushSnapshotAsync(string vmName, string snapshotId, string? feedId = null)
    {
        await PostAsync(
            "/api/vms/"
                + Uri.EscapeDataString(vmName)
                + "/snapshots/"
                + Uri.EscapeDataString(snapshotId)
                + "/push",
            feedId != null ? new { feedId } : null
        );
    }

    public async Task<string?> ApplyLocaleAsync(string vmName)
    {
        TaskResponse? result = await PostJsonAsync<TaskResponse>(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/apply-locale"
        );
        return result?.TaskId;
    }

    public async Task<List<VmImage>> GetCatalogAsync()
    {
        return await GetJsonAsync<List<VmImage>>("/api/catalog");
    }

    public async Task<string?> ImportVersionAsync(
        string versionRef,
        string safeFileName,
        VmImageVersion? version = null
    )
    {
        TaskResponse? result = await PostJsonAsync<TaskResponse>(
            "/api/catalog/import",
            new
            {
                versionRef,
                safeFileName,
                version,
            }
        );
        return result?.TaskId;
    }

    public async Task<string?> CreateVmAsync(
        string extractedFolder,
        string name,
        int memoryMb,
        int cpuCount,
        VmOrigin? origin = null,
        List<VmNetworkAdapter>? networks = null
    )
    {
        TaskResponse? result = await PostJsonAsync<TaskResponse>(
            "/api/catalog/create-vm",
            new
            {
                extractedFolder,
                name,
                memoryMb,
                cpuCount,
                origin,
                networks,
            }
        );
        return result?.TaskId;
    }

    public async Task<List<LocalImage>> GetLocalImagesAsync()
    {
        return await GetJsonAsync<List<LocalImage>>("/api/catalog/local");
    }

    public async Task DeleteLocalImageAsync(string path)
    {
        await DeleteAsync("/api/catalog/local?path=" + Uri.EscapeDataString(path));
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        return await GetJsonAsync<AppSettings>("/api/settings");
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await PutAsync("/api/settings", settings);
    }

    public async Task<bool> TestFeedAsync(FeedConfiguration feed)
    {
        FeedTestResult? result = await PostJsonAsync<FeedTestResult>(
            "/api/settings/feeds/test",
            feed
        );
        return result?.Success ?? false;
    }

    public async Task<List<string>> DiscoverRepositoriesAsync(FeedConfiguration feed)
    {
        return await PostJsonAsync<List<string>>("/api/settings/feeds/discover", feed)
            ?? new List<string>();
    }

    public async Task<EmailTestResult> TestEmailAsync(
        string toAddress,
        string smtpHost,
        int smtpPort,
        string smtpUsername,
        string smtpPassword,
        string smtpFromAddress,
        bool smtpUseTls
    )
    {
        return await PostJsonAsync<EmailTestResult>(
                "/api/settings/test-email",
                new
                {
                    toAddress,
                    smtpHost,
                    smtpPort,
                    smtpUsername,
                    smtpPassword,
                    smtpFromAddress,
                    smtpUseTls,
                }
            ) ?? new EmailTestResult { Success = false, Error = "No response" };
    }

    public async Task<QuotaUsage> GetMyQuotaAsync()
    {
        return await GetJsonAsync<QuotaUsage>("/api/settings/quota");
    }

    public async Task<QuotaUsage> GetUserQuotaAsync(string username)
    {
        return await GetJsonAsync<QuotaUsage>(
            "/api/users/" + Uri.EscapeDataString(username) + "/quota"
        );
    }

    public async Task SetUserQuotaAsync(string username, int maxVms)
    {
        await PutAsync("/api/users/" + Uri.EscapeDataString(username) + "/quota", new { maxVms });
    }

    public async Task<List<VmSessionGroup>> GetActiveSessionsAsync()
    {
        return await GetJsonAsync<List<VmSessionGroup>>("/api/sessions")
            ?? new List<VmSessionGroup>();
    }

    public async Task DisconnectSessionAsync(string vmName, string token)
    {
        await PostAsync(
            "/api/sessions/"
                + Uri.EscapeDataString(vmName)
                + "/"
                + Uri.EscapeDataString(token)
                + "/disconnect",
            null
        );
    }

    public async Task SendInviteEmailAsync(string username)
    {
        await PostAsync("/api/users/" + Uri.EscapeDataString(username) + "/send-invite", null);
    }

    public async Task UpdateUserEmailAsync(string username, string email)
    {
        await PutAsync("/api/users/" + Uri.EscapeDataString(username) + "/email", new { email });
    }

    public async Task ConnectToProgressHubAsync(
        Action<string, double, string> onProgress,
        Action<string, bool, string?> onCompleted,
        Func<Exception?, Task>? onClosed = null
    )
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(
                _baseUrl + "/hubs/progress",
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    };
                    if (_http.DefaultRequestHeaders.Authorization != null)
                    {
                        options.Headers["Authorization"] =
                            _http.DefaultRequestHeaders.Authorization.ToString();
                    }
                }
            )
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On("TaskProgress", onProgress);
        _hubConnection.On("TaskCompleted", onCompleted);

        if (onClosed != null)
            _hubConnection.Closed += onClosed;

        await _hubConnection.StartAsync();
    }

    public async Task<AgentTaskStatus?> GetTaskStatusAsync(string taskId)
    {
        try
        {
            List<AgentTaskStatus> tasks = await GetJsonAsync<List<AgentTaskStatus>>(
                "/api/status/tasks"
            );
            return tasks.FirstOrDefault(t => t.Id == taskId);
        }
        catch
        {
            return null;
        }
    }

    public async Task DisconnectProgressHubAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    public async Task<AuthenticatedUser> GetCurrentUserAsync()
    {
        return await GetJsonAsync<AuthenticatedUser>("/api/auth/me");
    }

    public async Task ChangeOwnPasswordAsync(string newPassword)
    {
        await PutAsync("/api/auth/password", new { newPassword });
    }

    public async Task<List<AuthenticatedUser>> GetUsersAsync()
    {
        return await GetJsonAsync<List<AuthenticatedUser>>("/api/users");
    }

    public async Task CreateUserAsync(
        string username,
        string password,
        HashSet<string> permissions,
        bool isAdmin
    )
    {
        await PostAsync(
            "/api/users",
            new
            {
                username,
                password,
                permissions,
                isAdmin,
            }
        );
    }

    public async Task DeleteUserAsync(string username)
    {
        await DeleteAsync("/api/users/" + Uri.EscapeDataString(username));
    }

    public async Task UpdateUserPermissionsAsync(
        string username,
        HashSet<string> permissions,
        bool? isAdmin = null
    )
    {
        await PutAsync(
            "/api/users/" + Uri.EscapeDataString(username) + "/permissions",
            new { permissions, isAdmin }
        );
    }

    public async Task RenameUserAsync(string username, string newUsername)
    {
        await PutAsync(
            "/api/users/" + Uri.EscapeDataString(username) + "/rename",
            new { newUsername }
        );
    }

    public async Task ResetUserPasswordAsync(string username, string newPassword)
    {
        await PutAsync(
            "/api/users/" + Uri.EscapeDataString(username) + "/password",
            new { newPassword }
        );
    }

    public async Task<List<VmShareEntry>> GetVmSharesAsync(string vmName)
    {
        return await GetJsonAsync<List<VmShareEntry>>(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/sharing"
        );
    }

    public async Task ShareVmAsync(string vmName, string username, HashSet<string> permissions)
    {
        await PostAsync(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/sharing",
            new { username, permissions }
        );
    }

    public async Task UnshareVmAsync(string vmName, string username)
    {
        await DeleteAsync(
            "/api/vms/"
                + Uri.EscapeDataString(vmName)
                + "/sharing/"
                + Uri.EscapeDataString(username)
        );
    }

    public async Task<RdpShadowSessionsResponse> GetShadowSessionsAsync(string vmName)
    {
        return await GetJsonAsync<RdpShadowSessionsResponse>(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/sessions"
        );
    }

    public void LaunchShadowSession(string vmName, int sessionId, bool noConsentPrompt)
    {
        string address = GetRdpProxyAddress(vmName);
        string arguments = noConsentPrompt
            ? "/v:" + address + " /shadow:" + sessionId + " /control /noConsentPrompt"
            : "/v:" + address + " /shadow:" + sessionId + " /control";
        Process.Start(new ProcessStartInfo("mstsc.exe", arguments) { UseShellExecute = true });
    }

    private string GetRdpProxyAddress(string vmName)
    {
        Uri baseUri = new Uri(_baseUrl);
        string host = baseUri.Host;
        int port = 13389;

        if (!string.IsNullOrEmpty(RdpProxyHost))
        {
            Uri proxyUri = new Uri(
                RdpProxyHost.Contains("://") ? RdpProxyHost : "tcp://" + RdpProxyHost
            );
            host = proxyUri.Host;
            if (proxyUri.Port > 0 && proxyUri.Port != 80)
                port = proxyUri.Port;
        }

        return host + ":" + port;
    }

    public async Task TransferVmOwnershipAsync(string vmName, string newOwnerUsername)
    {
        await PutAsync(
            "/api/vms/" + Uri.EscapeDataString(vmName) + "/sharing/transfer",
            new { newOwnerUsername }
        );
    }

    public async Task<bool> WaitForTaskAsync(
        string taskId,
        Action<double, string>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            completion.TrySetResult(false)
        );

        await ConnectToProgressHubAsync(
            (id, progress, status) =>
            {
                if (id != taskId)
                    return;
                onProgress?.Invoke(progress, status);
            },
            (id, success, _) =>
            {
                if (id != taskId)
                    return;
                completion.TrySetResult(success);
            }
        );

        bool result = await completion.Task;
        await DisconnectProgressHubAsync();
        return result;
    }

    private async Task<T> GetJsonAsync<T>(string url)
        where T : new()
    {
        try
        {
            HttpResponseMessage response = await _http.GetAsync(url);
            await EnsureSuccessAsync(response);
            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("API request failed for {Url}: {Message}", url, ex.Message);
            return new T();
        }
    }

    private async Task<T?> PostJsonAsync<T>(string url, object? body = null)
    {
        HttpResponseMessage response;
        if (body != null)
        {
            string requestJson = JsonSerializer.Serialize(body, JsonOptions);
            StringContent content = new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json"
            );
            response = await _http.PostAsync(url, content);
        }
        else
        {
            response = await _http.PostAsync(url, null);
        }
        await EnsureSuccessAsync(response);
        string responseJson = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseJson))
            return default;
        return JsonSerializer.Deserialize<T>(responseJson, JsonOptions);
    }

    private async Task PostAsync(string url, object? body = null)
    {
        HttpResponseMessage response;
        if (body != null)
        {
            string requestJson = JsonSerializer.Serialize(body, JsonOptions);
            StringContent content = new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json"
            );
            response = await _http.PostAsync(url, content);
        }
        else
        {
            response = await _http.PostAsync(url, null);
        }
        await EnsureSuccessAsync(response);
    }

    private async Task PutAsync(string url, object body)
    {
        string requestJson = JsonSerializer.Serialize(body, JsonOptions);
        StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await _http.PutAsync(url, content);
        await EnsureSuccessAsync(response);
    }

    private async Task DeleteAsync(string url)
    {
        HttpResponseMessage response = await _http.DeleteAsync(url);
        await EnsureSuccessAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = await response.Content.ReadAsStringAsync();
        string message = "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                ErrorResponse? error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
                if (error != null && !string.IsNullOrWhiteSpace(error.Error))
                {
                    message = error.Error;
                }
                else
                {
                    message = body;
                }
            }
            catch
            {
                message = body;
            }
        }

        throw new HttpRequestException(message);
    }

    public void Dispose()
    {
        _hubConnection?.DisposeAsync().AsTask().Wait();
        _http.Dispose();
    }

    private sealed class TaskResponse
    {
        public string? TaskId { get; set; }
        public string? Title { get; set; }
    }

    private sealed class FeedTestResult
    {
        public bool Success { get; set; }
    }

    private sealed class TroubleshootResponse
    {
        public string Report { get; set; } = "";
    }

    private sealed class ActiveSessionCountResponse
    {
        public int Count { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }

    private sealed class AgentStatusResponse
    {
        public string? Status { get; set; }
        public string? Version { get; set; }
        public string? Backend { get; set; }
    }
}

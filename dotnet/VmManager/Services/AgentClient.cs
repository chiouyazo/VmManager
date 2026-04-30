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

    public async Task<VmAccessEntry> GetVmAccessAsync(string name)
    {
        return await GetJsonAsync<VmAccessEntry>(
            "/api/vms/" + Uri.EscapeDataString(name) + "/access"
        );
    }

    public async Task SetVmAccessAsync(string name, string username, VmPermission permission)
    {
        await PutAsync(
            "/api/vms/" + Uri.EscapeDataString(name) + "/access/" + Uri.EscapeDataString(username),
            new { permission }
        );
    }

    public async Task RemoveVmAccessAsync(string name, string username)
    {
        await DeleteAsync(
            "/api/vms/" + Uri.EscapeDataString(name) + "/access/" + Uri.EscapeDataString(username)
        );
    }

    public async Task<TunnelSessionResponse> CreateTunnelSessionAsync(string vmName, int remotePort)
    {
        return await PostJsonAsync<TunnelSessionResponse>(
                "/api/tunnel-sessions/" + Uri.EscapeDataString(vmName) + "?remotePort=" + remotePort
            ) ?? throw new InvalidOperationException("Failed to create tunnel session");
    }

    public string GetTunnelWebSocketUrl(string token)
    {
        Uri uri = new(_baseUrl);
        string wsScheme = uri.Scheme == "https" ? "wss" : "ws";
        return $"{wsScheme}://{uri.Authority}/api/tunnel-sessions/{Uri.EscapeDataString(token)}/connect";
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

    public async Task ConnectToVmAsync(string name)
    {
        RdpSessionResponse? session = await PostJsonAsync<RdpSessionResponse>(
            "/api/rdp-sessions/" + Uri.EscapeDataString(name)
        );

        if (session == null || string.IsNullOrEmpty(session.Token))
            throw new InvalidOperationException("Failed to create RDP session for " + name);

        string rdpHost;
        int rdpPort;

        if (!string.IsNullOrEmpty(RdpProxyHost))
        {
            Uri rdpUri = new Uri(
                RdpProxyHost.Contains("://") ? RdpProxyHost : "tcp://" + RdpProxyHost
            );
            rdpHost = rdpUri.Host;
            rdpPort = rdpUri.Port > 0 && rdpUri.Port != 80 ? rdpUri.Port : session.RdpPort;
        }
        else
        {
            Uri baseUri = new Uri(_baseUrl);
            rdpHost = baseUri.Host;
            rdpPort = session.RdpPort;
        }

        string rdpContent = string.Join(
            "\r\n",
            "full address:s:" + rdpHost + ":" + rdpPort,
            "username:s:Administrator",
            "loadbalanceinfo:s:cookie: mstshash=" + session.Token,
            "autoreconnection enabled:i:1",
            "prompt for credentials:i:1",
            ""
        );

        string tempDir = Path.Combine(Path.GetTempPath(), "VmManager");
        Directory.CreateDirectory(tempDir);

        string[] oldFiles = Directory.GetFiles(tempDir, name + "*.rdp");
        foreach (string oldFile in oldFiles)
        {
            try
            {
                File.Delete(oldFile);
            }
            catch { }
        }

        string tempPath = Path.Combine(tempDir, name + "-" + session.Token[..8] + ".rdp");
        await File.WriteAllTextAsync(tempPath, rdpContent);

        LaunchRdpFile(tempPath);
    }

    private static void LaunchRdpFile(string rdpPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(
                new ProcessStartInfo("mstsc.exe", "/edit \"" + rdpPath + "\"")
                {
                    UseShellExecute = true,
                }
            );
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            string? app = FindMacOsRdpApp();
            if (app != null)
            {
                Process.Start("open", new[] { "-a", app, rdpPath });
                return;
            }
        }
        Process.Start(new ProcessStartInfo(rdpPath) { UseShellExecute = true });
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

    private sealed class RdpSessionResponse
    {
        public string Token { get; set; } = "";
        public string VmName { get; set; } = "";
        public int RdpPort { get; set; }
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

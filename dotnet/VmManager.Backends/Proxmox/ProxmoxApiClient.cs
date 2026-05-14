using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Backends.Proxmox;

public class ProxmoxApiClient
{
    private readonly HttpClient _http;
    private readonly ProxmoxSettings _settings;
    private readonly ILogger<ProxmoxApiClient> _logger;
    private readonly string _nodeBase;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProxmoxApiClient(ProxmoxSettings settings, ILogger<ProxmoxApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings;
        _logger = logger;
        _nodeBase = $"/api2/json/nodes/{settings.Node}";

        HttpClientHandler handler = new HttpClientHandler();
        if (!settings.VerifySsl)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.ApiUrl),
            Timeout = TimeSpan.FromMinutes(10),
        };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "PVEAPIToken",
                $"{settings.ApiTokenId}={settings.ApiTokenSecret}"
            );
    }

    public string Node => _settings.Node;
    public string StorageId => _settings.StorageId;
    public string PoolId => _settings.PoolId;
    public int MaxPoolMemoryMb => _settings.MaxPoolMemoryMb;
    public int MaxPoolCpuCores => _settings.MaxPoolCpuCores;

    public async Task<T> GetAsync<T>(string path)
    {
        using HttpResponseMessage resp = await _http.GetAsync(path);
        return await ParseResponseAsync<T>(resp, path);
    }

    public async Task<T> PostAsync<T>(string path, Dictionary<string, string>? formData = null)
    {
        using HttpContent? content = formData != null ? new FormUrlEncodedContent(formData) : null;
        using HttpResponseMessage resp = await _http.PostAsync(path, content);
        return await ParseResponseAsync<T>(resp, path);
    }

    public async Task<string> PostRawAsync(string path, Dictionary<string, string>? formData = null)
    {
        using HttpContent? content = formData != null ? new FormUrlEncodedContent(formData) : null;
        using HttpResponseMessage resp = await _http.PostAsync(path, content);
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new ProxmoxApiException((int)resp.StatusCode, body);
        return body;
    }

    public async Task PutAsync(string path, Dictionary<string, string> formData)
    {
        using HttpContent content = new FormUrlEncodedContent(formData);
        using HttpResponseMessage resp = await _http.PutAsync(path, content);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync();
            throw new ProxmoxApiException((int)resp.StatusCode, body);
        }
    }

    public async Task DeleteAsync(string path)
    {
        using HttpResponseMessage resp = await _http.DeleteAsync(path);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync();
            throw new ProxmoxApiException((int)resp.StatusCode, body);
        }
    }

    public async Task PollTaskAsync(string upid, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromMinutes(10);
        DateTime deadline = DateTime.UtcNow + timeout.Value;
        string taskPath = $"{_nodeBase}/tasks/{Uri.EscapeDataString(upid)}/status";

        while (DateTime.UtcNow < deadline)
        {
            JsonElement status = await GetAsync<JsonElement>(taskPath);
            string state = status.GetProperty("status").GetString() ?? "";
            if (state == "stopped")
            {
                string exitStatus = status.TryGetProperty("exitstatus", out JsonElement es)
                    ? es.GetString() ?? ""
                    : "";
                if (exitStatus == "OK")
                    return;
                throw new ProxmoxApiException(500, $"Task failed: {exitStatus}");
            }
            await Task.Delay(1000);
        }
        throw new TimeoutException(
            $"Proxmox task did not complete within {timeout.Value.TotalSeconds}s"
        );
    }

    public async Task<int> GetNextVmIdAsync()
    {
        JsonElement result = await GetAsync<JsonElement>("/api2/json/cluster/nextid");
        return int.Parse(
            result.GetString() ?? throw new InvalidOperationException("No VMID returned")
        );
    }

    public string VmPath(int vmid) => $"{_nodeBase}/qemu/{vmid}";

    private async Task<T> ParseResponseAsync<T>(HttpResponseMessage resp, string path)
    {
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new ProxmoxApiException((int)resp.StatusCode, body);

        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("data", out JsonElement data))
        {
            if (typeof(T) == typeof(JsonElement))
                return (T)(object)data.Clone();
            return data.Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize response from {path}"
                );
        }
        throw new InvalidOperationException($"No 'data' field in response from {path}");
    }
}

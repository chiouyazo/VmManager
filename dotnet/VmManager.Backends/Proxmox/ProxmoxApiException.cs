using System.Text.Json;

namespace VmManager.Backends.Proxmox;

public class ProxmoxApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public ProxmoxApiException(int statusCode, string responseBody)
        : base($"Proxmox API error ({statusCode}): {ExtractMessage(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    private static string ExtractMessage(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out JsonElement msg))
                return msg.GetString() ?? body;
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }
}

using System.Net;

namespace VmManager.Catalog;

public static class HttpErrorHelper
{
    public static async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response,
        string operation
    )
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = "";
        try
        {
            body = await response.Content.ReadAsStringAsync();
        }
        catch { }

        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                $"{operation}: Invalid credentials. Check your username and password in Settings.",
            HttpStatusCode.Forbidden =>
                $"{operation}: Permission denied. You don't have write access to this repository.",
            HttpStatusCode.NotFound =>
                $"{operation}: Repository or endpoint not found. Check the URL and repository name in Settings.",
            HttpStatusCode.Conflict => $"{operation}: Conflict. The resource may already exist.",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                $"{operation}: Request timed out. The server may be overloaded.",
            HttpStatusCode.InternalServerError => $"{operation}: Server error. Try again later.",
            HttpStatusCode.ServiceUnavailable =>
                $"{operation}: Service unavailable. The server may be down for maintenance.",
            _ =>
                $"{operation}: HTTP {(int)response.StatusCode} ({response.StatusCode}). {body}".TrimEnd(),
        };

        throw new InvalidOperationException(message);
    }

    public static string DescribeConnectivityFailure(string target, Exception? ex = null)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.InnerException is System.Net.Sockets.SocketException)
                return $"Cannot connect to {target}. Check the URL and your network connection.";
            if (httpEx.StatusCode == HttpStatusCode.Unauthorized)
                return $"{target} returned 401 Unauthorized. Check your credentials.";
            return $"Cannot reach {target}: {httpEx.Message}";
        }

        if (ex is TaskCanceledException)
            return $"Connection to {target} timed out. The server may be unreachable.";

        return $"Cannot reach {target}. Check your network and settings.";
    }
}

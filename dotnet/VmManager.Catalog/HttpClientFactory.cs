namespace VmManager.Catalog;

/// <summary>
/// Central factory for pre-configured HttpClient instances.
/// All clients bypass SSL certificate validation (required for self-signed internal servers).
/// </summary>
public static class CatalogHttpClientFactory
{
    private static SocketsHttpHandler CreateHandler() =>
        new SocketsHttpHandler()
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };

    /// <summary>Short-lived client for catalog API calls (30s timeout).</summary>
    public static HttpClient CreateCatalogClient() =>
        new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Long-lived client for large file uploads (60min timeout).</summary>
    public static HttpClient CreateUploadClient() =>
        new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromMinutes(60) };

    /// <summary>Long-lived client for large file downloads (4hr timeout).</summary>
    public static HttpClient CreateDownloadClient() =>
        new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromHours(4) };

    /// <summary>Short-lived client for connectivity tests (10s timeout).</summary>
    public static HttpClient CreateTestClient() =>
        new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) };
}

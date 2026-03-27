using System.Diagnostics;

namespace VmManager.Catalog.Shared;

public static class StreamCopyHelper
{
    public const int DefaultBufferSize = 4 * 1024 * 1024;

    public static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long totalBytes,
        Action<TransferProgress>? onProgress,
        CancellationToken ct,
        int bufferSize = DefaultBufferSize
    )
    {
        byte[] buffer = new byte[bufferSize];
        long copiedBytes = 0;
        Stopwatch sw = Stopwatch.StartNew();
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            copiedBytes += bytesRead;
            onProgress?.Invoke(new TransferProgress(copiedBytes, totalBytes, sw.Elapsed));
        }
    }

    public static async Task<HttpResponseMessage> SendWithUploadProgressAsync(
        HttpClient client,
        HttpRequestMessage request,
        FileStream bodyStream,
        long totalBytes,
        Action<TransferProgress>? onProgress,
        CancellationToken ct,
        int pollIntervalMs = 500
    )
    {
        request.Content = new StreamContent(bodyStream);
        Task<HttpResponseMessage> sendTask = client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        Stopwatch sw = Stopwatch.StartNew();

        while (!sendTask.IsCompleted)
        {
            await Task.Delay(pollIntervalMs, ct);
            long uploaded = bodyStream.Position;
            onProgress?.Invoke(new TransferProgress(uploaded, totalBytes, sw.Elapsed));
        }

        return await sendTask;
    }
}

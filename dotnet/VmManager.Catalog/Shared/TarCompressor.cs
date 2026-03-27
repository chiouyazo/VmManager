using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Shared;

/// <summary>
/// Compresses an exported snapshot directory into a tar.gz archive with progress reporting.
/// </summary>
public class TarCompressor
{
    private readonly ILogger<TarCompressor> _logger;

    public TarCompressor(ILogger<TarCompressor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Creates a tar.gz archive from the exported content in <paramref name="tempDir"/>.
    /// Returns the path to the resulting archive file.
    /// </summary>
    public async Task<string> CompressAsync(
        string tempDir,
        IProgress<PushProgress>? progress = null,
        CancellationToken ct = default
    )
    {
        string tarPath = Path.Combine(tempDir, CatalogConstants.SnapshotArchiveName);
        string exportedContent = Directory.GetDirectories(tempDir).FirstOrDefault() ?? tempDir;
        string tarExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "tar.exe"
        );

        long sourceSize = Directory
            .GetFiles(exportedContent, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        ProcessStartInfo psi = new ProcessStartInfo(tarExe)
        {
            Arguments = $"-cf - -C \"{exportedContent}\" .",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tar.exe");
        using (process)
        {
            try
            {
                long bytesRead = 0;
                Stopwatch sw = Stopwatch.StartNew();

                using (
                    FileStream outFile = new FileStream(
                        tarPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024
                    )
                )
                using (GZipStream gzip = new GZipStream(outFile, CompressionLevel.Fastest))
                {
                    byte[] buffer = new byte[4 * 1024 * 1024];
                    Stream stdout = process.StandardOutput.BaseStream;
                    int read;
                    double lastReport = 0;

                    while ((read = await stdout.ReadAsync(buffer, ct)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await gzip.WriteAsync(buffer.AsMemory(0, read), ct);
                        bytesRead += read;

                        if (sourceSize > 0 && sw.Elapsed.TotalSeconds - lastReport >= 0.5)
                        {
                            lastReport = sw.Elapsed.TotalSeconds;
                            double ratio = Math.Min(0.99, (double)bytesRead / sourceSize);
                            double percent = 5.0 + ratio * 35.0;
                            double speedMb =
                                bytesRead / Math.Max(1, sw.Elapsed.TotalSeconds) / 1024.0 / 1024.0;
                            double compressedMb = outFile.Position / 1024.0 / 1024.0;

                            double remainingBytes = sourceSize - bytesRead;
                            double bytesPerSec = bytesRead / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                            double etaSec = remainingBytes / Math.Max(1, bytesPerSec);
                            string eta =
                                etaSec < 60
                                    ? $"{etaSec:F0}s"
                                    : $"{etaSec / 60:F0}m {etaSec % 60:F0}s";

                            progress?.Report(
                                new PushProgress(
                                    $"Compressing... {ratio * 100:F0}%",
                                    percent,
                                    $"{compressedMb:F0} MB out - {speedMb:F0} MB/s - ~{eta} left"
                                )
                            );
                        }
                    }
                }

                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"tar failed: {await process.StandardError.ReadToEndAsync()}"
                    );
            }
            catch
            {
                // Kill tar.exe on cancellation or error so it doesn't keep running
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                { /* Cleanup failure is non-fatal */
                }
                throw;
            }
        }

        return tarPath;
    }
}

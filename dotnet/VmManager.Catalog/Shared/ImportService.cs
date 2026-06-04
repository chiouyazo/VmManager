using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using VmManager.Contracts.Models;

namespace VmManager.Catalog.Shared;

/// <summary>
/// Handles copying VM archive files from the network drive and extracting them locally.
/// Supports both ZIP archives (.zip) and gzip-compressed tar archives (.box, .tar.gz).
/// </summary>
public class ImportService
{
    private const int BufferSize = 4 * 1024 * 1024; // 4 MB copy buffer

    // Gzip magic bytes: 1F 8B
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];

    // ZIP magic bytes: PK (50 4B 03 04)
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Copies a file from source to destination while reporting progress (0-100).
    /// </summary>
    public async Task CopyWithProgressAsync(
        string sourcePath,
        string destPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default
    )
    {
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException($"Source file not found: {sourcePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        long totalBytes = new FileInfo(sourcePath).Length;
        long copiedBytes = 0;

        await using FileStream source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true
        );
        await using FileStream dest = new FileStream(
            destPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true
        );

        byte[] buffer = new byte[BufferSize];
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            copiedBytes += read;
            if (totalBytes > 0)
                progress?.Report(copiedBytes * 100.0 / totalBytes);
        }

        progress?.Report(100);
    }

    private static readonly HttpClient Http = CatalogHttpClientFactory.CreateDownloadClient();

    /// <summary>
    /// Downloads a file from an HTTP URL with rich progress reporting (speed, ETA).
    /// Supports cancellation and auth headers. Deletes partial file on cancel/error.
    /// </summary>
    public async Task DownloadWithProgressAsync(
        string url,
        string destPath,
        IProgress<DownloadProgress>? richProgress = null,
        CancellationToken ct = default,
        AuthenticationHeaderValue? auth = null
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null)
            request.Headers.Authorization = auth;

        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloadedBytes = 0;
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            await using Stream contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using FileStream fileStream = new FileStream(
                destPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true
            );

            byte[] buffer = new byte[BufferSize];
            int read;
            long lastReport = sw.ElapsedMilliseconds;

            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloadedBytes += read;

                // Report every ~500ms to avoid flooding the UI
                long now = sw.ElapsedMilliseconds;
                if (now - lastReport >= 500 || downloadedBytes == totalBytes)
                {
                    lastReport = now;
                    TimeSpan elapsed = sw.Elapsed;
                    double speed =
                        elapsed.TotalSeconds > 0 ? downloadedBytes / elapsed.TotalSeconds : 0;
                    double pct = totalBytes > 0 ? downloadedBytes * 100.0 / totalBytes : 0;
                    TimeSpan? eta =
                        speed > 0 && totalBytes > 0
                            ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / speed)
                            : (TimeSpan?)null;

                    richProgress?.Report(
                        new DownloadProgress(pct, downloadedBytes, totalBytes, speed, elapsed, eta)
                    );
                }
            }

            await fileStream.FlushAsync(ct);

            if (totalBytes > 0 && downloadedBytes != totalBytes)
                throw new InvalidOperationException(
                    $"Download incomplete: expected {totalBytes} bytes but received {downloadedBytes} bytes."
                );
        }
        catch
        {
            // Delete partial/corrupt file on error or cancellation
            try
            {
                File.Delete(destPath);
            }
            catch
            { /* non-fatal */
            }
            throw;
        }
    }

    /// <summary>
    /// Extracts an archive to a destination directory while reporting progress (0-100).
    /// Automatically detects gzip/tar (.box, .tar.gz) vs ZIP by reading the file header.
    /// </summary>
    public async Task ExtractAsync(
        string archivePath,
        string destDir,
        IProgress<double>? progress = null,
        CancellationToken ct = default
    )
    {
        if (!File.Exists(archivePath))
            throw new InvalidOperationException($"Archive not found: {archivePath}");

        Directory.CreateDirectory(destDir);

        ArchiveFormat format = await DetectFormatAsync(archivePath);

        if (format == ArchiveFormat.GzipTar)
            await ExtractTarGzAsync(archivePath, destDir, progress, ct);
        else if (format == ArchiveFormat.Zip)
            await ExtractZipAsync(archivePath, destDir, progress, ct);
        else
            throw new InvalidDataException(
                $"Unrecognised archive format for '{Path.GetFileName(archivePath)}'. "
                    + "Expected a ZIP or gzip-compressed tar (Vagrant .box) file."
            );
    }

    //  Format detection

    private enum ArchiveFormat
    {
        Unknown,
        GzipTar,
        Zip,
    }

    private static async Task<ArchiveFormat> DetectFormatAsync(string path)
    {
        byte[] header = new byte[4];
        await using FileStream fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        int read = await fs.ReadAsync(header.AsMemory(0, 4));
        if (read < 2)
            return ArchiveFormat.Unknown;

        if (header[0] == GzipMagic[0] && header[1] == GzipMagic[1])
            return ArchiveFormat.GzipTar;

        if (
            read >= 4
            && header[0] == ZipMagic[0]
            && header[1] == ZipMagic[1]
            && header[2] == ZipMagic[2]
            && header[3] == ZipMagic[3]
        )
            return ArchiveFormat.Zip;

        return ArchiveFormat.Unknown;
    }

    //  Extractors

    /// <summary>
    /// Extracts a gzip-compressed tar archive (Vagrant .box format) using the
    /// Windows-built-in tar.exe (available since Windows 10 1803).
    /// Progress is approximated while the process runs.
    /// </summary>
    private static async Task ExtractTarGzAsync(
        string archivePath,
        string destDir,
        IProgress<double>? progress,
        CancellationToken ct
    )
    {
        string tarExe;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            tarExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "tar.exe"
            );

            if (!File.Exists(tarExe))
                throw new InvalidOperationException("tar.exe not found on Windows.");
        }
        else
        {
            tarExe = "tar";
        }

        ProcessStartInfo psi = new ProcessStartInfo(tarExe)
        {
            // -x extract  -f file  -C target directory
            Arguments = $"-xf \"{archivePath}\" -C \"{destDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tar.exe.");

        using (process)
        {
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);

            // tar gives no granular progress; inch the bar forward while we wait
            // so the UI doesn't look frozen. Caps at 90 until extraction finishes.
            Task pulseTask = PulseProgressAsync(progress, fromPct: 0, toPct: 90, ct: ct);

            await process.WaitForExitAsync(ct);
            await pulseTask;

            string stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"tar extraction failed:\n{stderr}");
        }

        progress?.Report(100);
    }

    /// <summary>Slowly ticks progress from <paramref name="fromPct"/> to <paramref name="toPct"/> every 800 ms.</summary>
    private static async Task PulseProgressAsync(
        IProgress<double>? progress,
        double fromPct,
        double toPct,
        CancellationToken ct
    )
    {
        if (progress is null)
            return;

        double current = fromPct;
        while (current < toPct)
        {
            try
            {
                await Task.Delay(800, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Move faster when near the start, slow down toward the cap
            double step = Math.Max(0.5, (toPct - current) * 0.08);
            current = Math.Min(current + step, toPct);
            progress.Report(current);
        }
    }

    /// <summary>Extracts a ZIP archive.</summary>
    private static async Task ExtractZipAsync(
        string zipPath,
        string destDir,
        IProgress<double>? progress,
        CancellationToken ct
    )
    {
        await Task.Run(
            () =>
            {
                using ZipArchive archive = ZipFile.OpenRead(zipPath);
                int total = archive.Entries.Count;
                int done = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        done++;
                        continue;
                    }

                    string destFile = Path.GetFullPath(
                        Path.Combine(
                            destDir,
                            entry.FullName.Replace('/', Path.DirectorySeparatorChar)
                        )
                    );

                    if (!destFile.StartsWith(destDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    entry.ExtractToFile(destFile, overwrite: true);

                    done++;
                    if (total > 0)
                        progress?.Report(done * 100.0 / total);
                }

                progress?.Report(100);
            },
            ct
        );
    }
}

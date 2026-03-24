using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;

namespace VmManager.Services;

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
    /// Copies a file from source to destination while reporting progress (0–100).
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

        var totalBytes = new FileInfo(sourcePath).Length;
        long copiedBytes = 0;

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true
        );
        await using var dest = new FileStream(
            destPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true
        );

        var buffer = new byte[BufferSize];
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

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromHours(4) };

    /// <summary>
    /// Downloads a file from an HTTP URL with rich progress reporting (speed, ETA).
    /// Supports cancellation and auth headers. Deletes partial file on cancel/error.
    /// </summary>
    public async Task DownloadWithProgressAsync(
        string url,
        string destPath,
        IProgress<Models.DownloadProgress>? richProgress = null,
        CancellationToken ct = default,
        AuthenticationHeaderValue? auth = null
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (auth != null)
            request.Headers.Authorization = auth;

        using var response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloadedBytes = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                destPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true
            );

            var buffer = new byte[BufferSize];
            int read;
            var lastReport = sw.ElapsedMilliseconds;

            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloadedBytes += read;

                // Report every ~500ms to avoid flooding the UI
                var now = sw.ElapsedMilliseconds;
                if (now - lastReport >= 500 || downloadedBytes == totalBytes)
                {
                    lastReport = now;
                    var elapsed = sw.Elapsed;
                    var speed =
                        elapsed.TotalSeconds > 0 ? downloadedBytes / elapsed.TotalSeconds : 0;
                    var pct = totalBytes > 0 ? downloadedBytes * 100.0 / totalBytes : 0;
                    var eta =
                        speed > 0 && totalBytes > 0
                            ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / speed)
                            : (TimeSpan?)null;

                    richProgress?.Report(
                        new Models.DownloadProgress(
                            pct,
                            downloadedBytes,
                            totalBytes,
                            speed,
                            elapsed,
                            eta
                        )
                    );
                }
            }
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
    /// Extracts an archive to a destination directory while reporting progress (0–100).
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

        var format = await DetectFormatAsync(archivePath);

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

    // ── Format detection ─────────────────────────────────────────────────────

    private enum ArchiveFormat
    {
        Unknown,
        GzipTar,
        Zip,
    }

    private static async Task<ArchiveFormat> DetectFormatAsync(string path)
    {
        var header = new byte[4];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = await fs.ReadAsync(header.AsMemory(0, 4));
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

    // ── Extractors ───────────────────────────────────────────────────────────

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
        // tar.exe ships with Windows 10 1803+ in System32
        var tarExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "tar.exe"
        );
        if (!File.Exists(tarExe))
            throw new InvalidOperationException(
                "tar.exe not found. Windows 10 version 1803 or later is required to extract .box files."
            );

        var psi = new ProcessStartInfo(tarExe)
        {
            // -x extract  -f file  -C target directory
            Arguments = $"-xf \"{archivePath}\" -C \"{destDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tar.exe.");

        using (process)
        {
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            // tar gives no granular progress; inch the bar forward while we wait
            // so the UI doesn't look frozen. Caps at 90 until extraction finishes.
            var pulseTask = PulseProgressAsync(progress, fromPct: 0, toPct: 90, ct: ct);

            await process.WaitForExitAsync(ct);
            await pulseTask;

            var stderr = await stderrTask;
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

        var current = fromPct;
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
            var step = Math.Max(0.5, (toPct - current) * 0.08);
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
                using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
                var total = archive.Entries.Count;
                var done = 0;

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        done++;
                        continue;
                    }

                    var destFile = Path.GetFullPath(
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

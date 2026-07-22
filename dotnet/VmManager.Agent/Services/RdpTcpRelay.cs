using System.Buffers;
using System.Diagnostics;

namespace VmManager.Agent.Services;

public class RdpTcpRelay
{
    private const int BufferSize = 64 * 1024;

    private readonly ILogger<RdpTcpRelay> _logger;

    public RdpTcpRelay(ILogger<RdpTcpRelay> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task RelayAsync(
        Stream clientStream,
        Stream targetStream,
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        long startTimestamp = Stopwatch.GetTimestamp();

        Task<PipeResult> clientToTarget = PipeAsync(
            clientStream,
            targetStream,
            "client->vm",
            connectionId,
            linkedCts
        );
        Task<PipeResult> targetToClient = PipeAsync(
            targetStream,
            clientStream,
            "vm->client",
            connectionId,
            linkedCts
        );

        try
        {
            await Task.WhenAll(clientToTarget, targetToClient);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{ConnectionId}] Relay ended with error", connectionId);
        }

        PipeResult c2t = await SafeResult(clientToTarget);
        PipeResult t2c = await SafeResult(targetToClient);
        TimeSpan duration = Stopwatch.GetElapsedTime(startTimestamp);

        _logger.LogInformation(
            "[{ConnectionId}] Relay ended after {Duration}: client->vm {C2tBytes} bytes ({C2tReason}), vm->client {T2cBytes} bytes ({T2cReason})",
            connectionId,
            duration,
            c2t.Bytes,
            c2t.Reason,
            t2c.Bytes,
            t2c.Reason
        );
    }

    private static async Task<PipeResult> SafeResult(Task<PipeResult> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return new PipeResult(0, "faulted");
        }
    }

    private async Task<PipeResult> PipeAsync(
        Stream source,
        Stream destination,
        string direction,
        string connectionId,
        CancellationTokenSource linkedCts
    )
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long total = 0;
        string reason = "completed";

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                int bytesRead = await source.ReadAsync(buffer, linkedCts.Token);
                if (bytesRead == 0)
                {
                    reason = "peer closed";
                    break;
                }

                total += bytesRead;
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), linkedCts.Token);
                await destination.FlushAsync(linkedCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            reason = "cancelled";
        }
        catch (IOException ex)
        {
            reason = "io error: " + ex.Message;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await linkedCts.CancelAsync();
        }

        _logger.LogDebug(
            "[{ConnectionId}] {Direction} pipe ended: {Bytes} bytes, {Reason}",
            connectionId,
            direction,
            total,
            reason
        );
        return new PipeResult(total, reason);
    }

    private readonly record struct PipeResult(long Bytes, string Reason);
}

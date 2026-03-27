namespace VmManager.Agent.Services;

public class RdpTcpRelay
{
    private const int BufferSize = 8192;

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

        Task clientToTarget = PipeAsync(
            clientStream,
            targetStream,
            "client->vm",
            connectionId,
            linkedCts
        );
        Task targetToClient = PipeAsync(
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
    }

    private async Task PipeAsync(
        Stream source,
        Stream destination,
        string direction,
        string connectionId,
        CancellationTokenSource linkedCts
    )
    {
        byte[] buffer = new byte[BufferSize];

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                int bytesRead = await source.ReadAsync(buffer, linkedCts.Token);
                if (bytesRead == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), linkedCts.Token);
                await destination.FlushAsync(linkedCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            await linkedCts.CancelAsync();
        }
    }
}

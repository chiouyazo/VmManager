using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VmManager.Agent.Services.Rdp;

namespace VmManager.Agent.Services;

public sealed class RdpProxyListener
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly RdpCredSspConnectionHandler _rdpHandler;
    private readonly ILogger<RdpProxyListener> _logger;
    private readonly ConcurrentDictionary<Task, byte> _activeConnections = new();
    private TcpListener? _listener;
    private SemaphoreSlim? _connectionLimit;

    public RdpProxyListener(
        RdpCredSspConnectionHandler rdpHandler,
        ILogger<RdpProxyListener> logger
    )
    {
        ArgumentNullException.ThrowIfNull(rdpHandler);
        ArgumentNullException.ThrowIfNull(logger);
        _rdpHandler = rdpHandler;
        _logger = logger;
    }

    public async Task StartAsync(int port, int maxConnections, CancellationToken cancellationToken)
    {
        _connectionLimit = new SemaphoreSlim(maxConnections, maxConnections);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _logger.LogInformation(
            "RDP proxy listening on port {Port} (max {Max} concurrent connections)",
            port,
            maxConnections
        );

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception acceptEx)
                {
                    _logger.LogError(acceptEx, "RDP proxy accept failed");
                    continue;
                }

                // Reject immediately when saturated rather than letting connections pile up unbounded.
                if (!await _connectionLimit.WaitAsync(0))
                {
                    _logger.LogWarning(
                        "RDP proxy at capacity ({Max}); rejecting connection from {Remote}",
                        maxConnections,
                        client.Client.RemoteEndPoint
                    );
                    client.Dispose();
                    continue;
                }

                ProxySocketTuning.Apply(client.Client, _logger);

                _logger.LogInformation(
                    "RDP proxy accepted TCP connection from {Remote}",
                    client.Client.RemoteEndPoint
                );
                TrackConnection(client, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDP proxy listener loop crashed");
        }
        finally
        {
            _listener.Stop();
            await DrainAsync();
            _logger.LogInformation("RDP proxy stopped");
        }
    }

    private void TrackConnection(TcpClient client, CancellationToken cancellationToken)
    {
        Task task = HandleConnectionAsync(client, cancellationToken);
        _activeConnections.TryAdd(task, 0);
        _ = task.ContinueWith(
            completed =>
            {
                _activeConnections.TryRemove(completed, out _);
                _connectionLimit!.Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private async Task DrainAsync()
    {
        Task[] pending = _activeConnections.Keys.ToArray();
        if (pending.Length == 0)
            return;

        _logger.LogInformation(
            "Waiting for {Count} active RDP connection(s) to drain...",
            pending.Length
        );
        try
        {
            await Task.WhenAll(pending).WaitAsync(DrainTimeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Drain timed out; {Count} connection(s) still active",
                _activeConnections.Count
            );
        }
        catch
        {
            // Individual connection failures are already logged by HandleConnectionAsync.
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                await _rdpHandler.HandleConnectionAsync(stream, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "RDP proxy connection from {Remote} failed",
                client.Client.RemoteEndPoint
            );
        }
    }
}

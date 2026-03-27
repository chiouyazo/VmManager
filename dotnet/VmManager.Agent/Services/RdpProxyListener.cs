using System.Net;
using System.Net.Sockets;

namespace VmManager.Agent.Services;

public sealed class RdpProxyListener
{
    private readonly RdpConnectionHandler _rdpHandler;
    private readonly ILogger<RdpProxyListener> _logger;
    private TcpListener? _listener;

    public RdpProxyListener(RdpConnectionHandler rdpHandler, ILogger<RdpProxyListener> logger)
    {
        ArgumentNullException.ThrowIfNull(rdpHandler);
        ArgumentNullException.ThrowIfNull(logger);
        _rdpHandler = rdpHandler;
        _logger = logger;
    }

    public async Task StartAsync(int port, CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _logger.LogInformation("RDP proxy listening on port {Port}", port);

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

                _logger.LogInformation(
                    "RDP proxy accepted TCP connection from {Remote}",
                    client.Client.RemoteEndPoint
                );
                _ = HandleConnectionAsync(client, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDP proxy listener loop crashed");
        }
        finally
        {
            _listener.Stop();
            _logger.LogInformation("RDP proxy stopped");
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                await _rdpHandler.HandleRdpConnectionAsync(stream, cancellationToken);
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

using System.Net.Sockets;

namespace VmManager.Agent.Services.Rdp;

/// <summary>
/// Shared socket tuning for the RDP proxy. Disables Nagle's algorithm (RDP sends many
/// small, latency-sensitive packets, which Nagle would batch into 40-200ms stalls) and
/// enables TCP keep-alive so a dead peer (VM crash, dropped client, NAT/Wi-Fi timeout) is
/// detected by the OS instead of leaving a relay pipe blocked on a read that never returns.
/// </summary>
public static class ProxySocketTuning
{
    public static void Apply(Socket socket, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(logger);

        socket.NoDelay = true;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        try
        {
            // Start probing after 30s idle, probe every 10s, drop after 5 failed probes (~80s).
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
            socket.SetSocketOption(
                SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveInterval,
                10
            );
            socket.SetSocketOption(
                SocketOptionLevel.Tcp,
                SocketOptionName.TcpKeepAliveRetryCount,
                5
            );
        }
        catch (SocketException ex)
        {
            logger.LogDebug(
                ex,
                "TCP keep-alive tuning not supported on this platform; using OS defaults"
            );
        }
    }
}

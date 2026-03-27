using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace VmManager.Agent.Services;

public sealed class RdpConnectionHandler
{
    private readonly ILogger<RdpConnectionHandler> _logger;
    private readonly RdpSessionStore _sessionStore;
    private readonly RdpTcpRelay _relay;

    public RdpConnectionHandler(
        ILogger<RdpConnectionHandler> logger,
        RdpSessionStore sessionStore,
        RdpTcpRelay relay
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(relay);
        _logger = logger;
        _sessionStore = sessionStore;
        _relay = relay;
    }

    public async Task HandleRdpConnectionAsync(
        Stream clientStream,
        CancellationToken cancellationToken
    )
    {
        byte[] tpktHeader = new byte[4];
        await ReadExactAsync(clientStream, tpktHeader, cancellationToken);

        int totalLength = (tpktHeader[2] << 8) | tpktHeader[3];
        int payloadLength = totalLength - 4;

        byte[] payload = new byte[payloadLength];
        await ReadExactAsync(clientStream, payload, cancellationToken);

        string? token = ParseMstshash(payload);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("RDP connection without mstshash token, closing");
            return;
        }

        RdpSession? session = _sessionStore.ValidateAndActivate(token);
        if (session == null)
        {
            _logger.LogWarning("RDP connection with invalid token, closing");
            return;
        }

        try
        {
            _logger.LogInformation(
                "RDP proxy connecting to VM {VmName} at {VmIp}",
                session.VmName,
                session.VmIp
            );

            using TcpClient target = new TcpClient();
            await target.ConnectAsync(session.VmIp, 3389, cancellationToken);
            NetworkStream targetStream = target.GetStream();

            byte[] fullRequest = new byte[totalLength];
            Buffer.BlockCopy(tpktHeader, 0, fullRequest, 0, 4);
            Buffer.BlockCopy(payload, 0, fullRequest, 4, payloadLength);
            await targetStream.WriteAsync(fullRequest, cancellationToken);
            await targetStream.FlushAsync(cancellationToken);

            string connectionId = session.VmName + "-" + Guid.NewGuid().ToString("N")[..8];
            await _relay.RelayAsync(clientStream, targetStream, connectionId, cancellationToken);
        }
        finally
        {
            _sessionStore.CompleteSession(token);
            _logger.LogInformation("RDP session ended for VM {VmName}", session.VmName);
        }
    }

    private static string? ParseMstshash(byte[] x224Payload)
    {
        string text = Encoding.ASCII.GetString(x224Payload);
        Match match = Regex.Match(text, @"mstshash=([^\r\n]+)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new IOException("Connection closed during RDP handshake");
            offset += read;
        }
    }
}

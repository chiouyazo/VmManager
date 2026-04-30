using System.Net.WebSockets;

namespace VmManager.Agent.Services;

public static class WebSocketTcpBridge
{
    private const int BufferSize = 8192;

    public static async Task RelayAsync(WebSocket ws, Stream tcpStream, CancellationToken ct)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task wsToTcp = Task.Run(
            async () =>
            {
                byte[] buf = new byte[BufferSize];
                try
                {
                    while (!linked.Token.IsCancellationRequested)
                    {
                        WebSocketReceiveResult result = await ws.ReceiveAsync(buf, linked.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        await tcpStream.WriteAsync(buf.AsMemory(0, result.Count), linked.Token);
                        await tcpStream.FlushAsync(linked.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            },
            linked.Token
        );

        Task tcpToWs = Task.Run(
            async () =>
            {
                byte[] buf = new byte[BufferSize];
                try
                {
                    while (!linked.Token.IsCancellationRequested)
                    {
                        int read = await tcpStream.ReadAsync(buf, linked.Token);
                        if (read == 0)
                            break;
                        await ws.SendAsync(
                            buf.AsMemory(0, read),
                            WebSocketMessageType.Binary,
                            true,
                            linked.Token
                        );
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            },
            linked.Token
        );

        await Task.WhenAny(wsToTcp, tcpToWs);
        await linked.CancelAsync();
    }
}

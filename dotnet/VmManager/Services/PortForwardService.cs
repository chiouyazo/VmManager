using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VmManager.Services;

public sealed class PortForwardService
{
    private static readonly string PersistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VmManager",
        "port-forwards.json"
    );

    public ObservableCollection<PortForward> ActiveForwards { get; } = [];

    public async Task<PortForward> StartForwardAsync(
        AgentClient agent,
        string vmName,
        int remotePort,
        int localPort
    )
    {
        if (ActiveForwards.Any(f => f.LocalPort == localPort))
            throw new InvalidOperationException($"Local port {localPort} is already forwarded");

        try
        {
            using TcpListener probe = new(IPAddress.Loopback, localPort);
            probe.Start();
            probe.Stop();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException(
                $"Local port {localPort} is already in use by another application"
            );
        }

        TunnelSessionResponse response = await agent.CreateTunnelSessionAsync(vmName, remotePort);

        TcpListener listener = new(IPAddress.Loopback, localPort);
        listener.Start();

        CancellationTokenSource cts = new();
        PortForward forward = new(vmName, remotePort, localPort, listener, cts, response.Token);
        ActiveForwards.Add(forward);
        SaveState();

        _ = Task.Run(
            async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        TcpClient local = await listener.AcceptTcpClientAsync(cts.Token);
                        _ = RelayConnectionAsync(agent, response.Token, local, cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
                finally
                {
                    listener.Stop();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ActiveForwards.Remove(forward);
                        SaveState();
                    });
                }
            },
            cts.Token
        );

        return forward;
    }

    public void StopForward(PortForward forward)
    {
        forward.Dispose();
        ActiveForwards.Remove(forward);
        SaveState();
    }

    public void StopAll()
    {
        foreach (PortForward f in ActiveForwards.ToList())
            f.Dispose();
        ActiveForwards.Clear();
        SaveState();
    }

    public async Task RestoreForwardsAsync(AgentClient agent)
    {
        List<SavedForward> saved = LoadState();
        foreach (SavedForward s in saved)
        {
            try
            {
                await StartForwardAsync(agent, s.VmName, s.RemotePort, s.LocalPort);
            }
            catch
            {
                // VM might not be running, port might be taken
            }
        }
    }

    private void SaveState()
    {
        try
        {
            List<SavedForward> state = ActiveForwards
                .Select(f => new SavedForward
                {
                    VmName = f.VmName,
                    RemotePort = f.RemotePort,
                    LocalPort = f.LocalPort,
                })
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(PersistPath)!);
            File.WriteAllText(PersistPath, JsonSerializer.Serialize(state));
        }
        catch { }
    }

    private static List<SavedForward> LoadState()
    {
        try
        {
            if (File.Exists(PersistPath))
                return JsonSerializer.Deserialize<List<SavedForward>>(File.ReadAllText(PersistPath))
                    ?? [];
        }
        catch { }
        return [];
    }

    private static async Task RelayConnectionAsync(
        AgentClient agent,
        string token,
        TcpClient local,
        CancellationToken ct
    )
    {
        try
        {
            await using NetworkStream localStream = local.GetStream();
            ClientWebSocket ws = new();
            await ws.ConnectAsync(new Uri(agent.GetTunnelWebSocketUrl(token)), ct);

            byte[] buf = new byte[8192];
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                ct
            );

            Task localToWs = Task.Run(
                async () =>
                {
                    try
                    {
                        while (!linked.Token.IsCancellationRequested)
                        {
                            int read = await localStream.ReadAsync(buf, linked.Token);
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
                    catch { }
                },
                linked.Token
            );

            Task wsToLocal = Task.Run(
                async () =>
                {
                    byte[] wsBuf = new byte[8192];
                    try
                    {
                        while (!linked.Token.IsCancellationRequested)
                        {
                            WebSocketReceiveResult result = await ws.ReceiveAsync(
                                wsBuf,
                                linked.Token
                            );
                            if (result.MessageType == WebSocketMessageType.Close)
                                break;
                            await localStream.WriteAsync(
                                wsBuf.AsMemory(0, result.Count),
                                linked.Token
                            );
                            await localStream.FlushAsync(linked.Token);
                        }
                    }
                    catch { }
                },
                linked.Token
            );

            await Task.WhenAny(localToWs, wsToLocal);
            await linked.CancelAsync();

            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    null,
                    CancellationToken.None
                );
        }
        catch { }
        finally
        {
            local.Dispose();
        }
    }
}

public sealed class PortForward : IDisposable
{
    public string VmName { get; }
    public int RemotePort { get; }
    public int LocalPort { get; }
    public string Token { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts;

    public PortForward(
        string vmName,
        int remotePort,
        int localPort,
        TcpListener listener,
        CancellationTokenSource cts,
        string token
    )
    {
        VmName = vmName;
        RemotePort = remotePort;
        LocalPort = localPort;
        _listener = listener;
        _cts = cts;
        Token = token;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}

internal class SavedForward
{
    public string VmName { get; set; } = "";
    public int RemotePort { get; set; }
    public int LocalPort { get; set; }
}

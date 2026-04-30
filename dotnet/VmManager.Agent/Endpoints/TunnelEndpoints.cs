using System.Net.Sockets;
using System.Net.WebSockets;
using VmManager.Agent.Middleware;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Endpoints;

public static class TunnelEndpoints
{
    public static IEndpointRouteBuilder MapTunnelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/api/tunnel-sessions/{vmName}",
                async (
                    HttpContext context,
                    string vmName,
                    int remotePort,
                    IVmIpResolver resolver,
                    TunnelSessionStore sessionStore,
                    VmAuthorizationService authService
                ) =>
                {
                    if (!authService.CanPerform(context.GetVmUser(), vmName, VmPermission.Connect))
                        return Results.Forbid();

                    string? ip = await resolver.ResolveIpAsync(vmName, context.RequestAborted);
                    if (ip == null)
                        return Results.Json(
                            new { error = "VM not found or IP not available" },
                            statusCode: 503
                        );

                    try
                    {
                        using TcpClient probe = new();
                        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
                        await probe.ConnectAsync(ip, remotePort, cts.Token);
                    }
                    catch
                    {
                        return Results.Json(
                            new { error = $"Port {remotePort} not reachable on VM" },
                            statusCode: 503
                        );
                    }

                    TunnelSession session = sessionStore.CreateSession(vmName, ip, remotePort);
                    return Results.Ok(
                        new
                        {
                            token = session.Token,
                            vmName,
                            remotePort,
                        }
                    );
                }
            )
            .WithSummary("Create a tunnel session for port forwarding to a VM.");

        endpoints
            .MapGet(
                "/api/tunnel-sessions/{token}/connect",
                async (HttpContext context, string token, TunnelSessionStore sessionStore) =>
                {
                    if (!context.WebSockets.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 400;
                        return;
                    }

                    TunnelSession? session = sessionStore.ValidateAndActivate(token);
                    if (session == null)
                    {
                        context.Response.StatusCode = 401;
                        return;
                    }

                    WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();

                    try
                    {
                        using TcpClient target = new();
                        await target.ConnectAsync(session.VmIp, session.RemotePort);
                        await using NetworkStream targetStream = target.GetStream();
                        await WebSocketTcpBridge.RelayAsync(
                            ws,
                            targetStream,
                            context.RequestAborted
                        );
                    }
                    catch (SocketException) { }
                    finally
                    {
                        sessionStore.CompleteSession(token);
                        if (ws.State == WebSocketState.Open)
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                null,
                                CancellationToken.None
                            );
                    }
                }
            )
            .WithSummary("WebSocket endpoint for tunnel relay. Connect after creating a session.");

        endpoints
            .MapGet(
                "/api/tunnel-sessions",
                (TunnelSessionStore store) =>
                {
                    return Results.Ok(
                        store
                            .GetActiveSessions()
                            .Select(s => new
                            {
                                s.VmName,
                                s.RemotePort,
                                s.CreatedAt,
                                tokenPrefix = s.Token[..8] + "...",
                            })
                    );
                }
            )
            .WithSummary("List active tunnel sessions.");

        return endpoints;
    }
}

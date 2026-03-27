using System.Net.Sockets;
using VmManager.Agent.Services;

namespace VmManager.Agent.Endpoints;

public static class RdpEndpoints
{
    public static IEndpointRouteBuilder MapRdpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/api/rdp-sessions/{vmName}",
                async (
                    HttpContext context,
                    string vmName,
                    IVmIpResolver resolver,
                    RdpSessionStore sessionStore,
                    IConfiguration config
                ) =>
                {
                    string? ip = await resolver.ResolveIpAsync(vmName, context.RequestAborted);
                    if (ip == null)
                    {
                        return Results.Json(
                            new { error = "VM not found or IP not available. Is the VM running?" },
                            statusCode: 503
                        );
                    }

                    try
                    {
                        using System.Net.Sockets.TcpClient probe = new TcpClient();
                        using CancellationTokenSource cts = new CancellationTokenSource(
                            TimeSpan.FromSeconds(3)
                        );
                        await probe.ConnectAsync(ip, 3389, cts.Token);
                    }
                    catch
                    {
                        return Results.Json(
                            new
                            {
                                error = "RDP port 3389 is not reachable on the VM. Is Remote Desktop enabled?",
                            },
                            statusCode: 503
                        );
                    }

                    RdpSession session = sessionStore.CreateSession(vmName, ip);
                    int rdpPort = config.GetValue("VmManager:HttpPort", 18275);

                    return Results.Ok(
                        new
                        {
                            token = session.Token,
                            vmName = session.VmName,
                            rdpPort,
                        }
                    );
                }
            )
            .WithSummary(
                "Create an RDP session token for a VM. The token is used as the routing cookie for the RDP proxy port."
            );

        endpoints
            .MapGet(
                "/api/rdp-sessions",
                (RdpSessionStore sessionStore) =>
                {
                    IReadOnlyList<RdpSession> sessions = sessionStore.GetAllSessions();
                    return Results.Ok(
                        sessions.Select(s => new
                        {
                            s.VmName,
                            s.State,
                            s.CreatedAt,
                            tokenPrefix = s.Token[..8] + "...",
                        })
                    );
                }
            )
            .WithSummary("List all active and recent RDP sessions.");

        return endpoints;
    }
}

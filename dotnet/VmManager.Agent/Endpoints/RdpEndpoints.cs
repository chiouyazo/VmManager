using VmManager.Agent.Services;

namespace VmManager.Agent.Endpoints;

public static class RdpEndpoints
{
    public static IEndpointRouteBuilder MapRdpEndpoints(this IEndpointRouteBuilder endpoints)
    {
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
                            s.Username,
                            s.State,
                            s.CreatedAt,
                            s.CompletedAt,
                        })
                    );
                }
            )
            .RequireAuthorization();

        return endpoints;
    }
}

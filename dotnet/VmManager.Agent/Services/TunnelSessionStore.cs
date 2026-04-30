using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VmManager.Agent.Services;

public class TunnelSessionStore
{
    private readonly ConcurrentDictionary<string, TunnelSession> _sessions = new();
    private readonly Timer _cleanupTimer;

    private static readonly TimeSpan PendingExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CompletedExpiry = TimeSpan.FromMinutes(1);

    public TunnelSessionStore()
    {
        _cleanupTimer = new Timer(
            _ => Cleanup(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1)
        );
    }

    public TunnelSession CreateSession(string vmName, string vmIp, int remotePort)
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert
            .ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        TunnelSession session = new()
        {
            Token = token,
            VmName = vmName,
            VmIp = vmIp,
            RemotePort = remotePort,
            CreatedAt = DateTimeOffset.UtcNow,
            State = TunnelSessionState.Pending,
        };

        _sessions[token] = session;
        return session;
    }

    public TunnelSession? ValidateAndActivate(string token)
    {
        if (!_sessions.TryGetValue(token, out TunnelSession? session))
            return null;
        if (session.State == TunnelSessionState.Completed)
            return null;
        if (
            session.State == TunnelSessionState.Pending
            && DateTimeOffset.UtcNow - session.CreatedAt > PendingExpiry
        )
            return null;
        session.State = TunnelSessionState.Active;
        return session;
    }

    public void CompleteSession(string token)
    {
        if (_sessions.TryGetValue(token, out TunnelSession? session))
        {
            session.State = TunnelSessionState.Completed;
            session.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public IReadOnlyList<TunnelSession> GetActiveSessions() =>
        _sessions.Values.Where(s => s.State == TunnelSessionState.Active).ToList();

    private void Cleanup()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var kvp in _sessions)
        {
            TunnelSession s = kvp.Value;
            bool expired =
                (s.State == TunnelSessionState.Pending && now - s.CreatedAt > PendingExpiry)
                || (
                    s.State == TunnelSessionState.Completed
                    && now - (s.CompletedAt ?? s.CreatedAt) > CompletedExpiry
                );
            if (expired)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }
}

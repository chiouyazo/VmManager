using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VmManager.Agent.Services;

public sealed class RdpSessionStore : IDisposable
{
    private static readonly TimeSpan PendingExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReconnectGracePeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, RdpSession> _sessions =
        new ConcurrentDictionary<string, RdpSession>();
    private readonly ILogger<RdpSessionStore> _logger;
    private readonly Timer _cleanupTimer;

    public RdpSessionStore(ILogger<RdpSessionStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cleanupTimer = new Timer(
            _ => CleanupExpired(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1)
        );
    }

    public RdpSession CreateSession(string vmName, string vmIp, string username = "")
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert
            .ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        RdpSession session = new RdpSession
        {
            Token = token,
            VmName = vmName,
            VmIp = vmIp,
            Username = username,
            CreatedAt = DateTimeOffset.UtcNow,
            State = RdpSessionState.Pending,
        };

        _sessions[token] = session;
        _logger.LogInformation(
            "RDP session created for VM {VmName} at {VmIp}, token={TokenPrefix}...",
            vmName,
            vmIp,
            token[..8]
        );

        return session;
    }

    public RdpSession? ValidateAndActivate(string token)
    {
        if (!_sessions.TryGetValue(token, out RdpSession? session))
        {
            _logger.LogWarning(
                "RDP session token not found: {TokenPrefix}...",
                token.Length > 8 ? token[..8] : token
            );
            return null;
        }

        if (session.State == RdpSessionState.Pending)
        {
            if (DateTimeOffset.UtcNow - session.CreatedAt > PendingExpiry)
            {
                _logger.LogWarning("RDP session token {TokenPrefix}... expired", token[..8]);
                _sessions.TryRemove(token, out _);
                return null;
            }

            session.State = RdpSessionState.Active;
            _logger.LogInformation(
                "RDP session activated for VM {VmName}, token={TokenPrefix}...",
                session.VmName,
                token[..8]
            );
            return session;
        }

        if (session.State == RdpSessionState.Active || session.State == RdpSessionState.Completed)
        {
            if (
                session.CompletedAt.HasValue
                && DateTimeOffset.UtcNow - session.CompletedAt.Value > ReconnectGracePeriod
            )
            {
                _logger.LogWarning(
                    "RDP session token {TokenPrefix}... grace period expired, rejecting",
                    token[..8]
                );
                return null;
            }

            session.State = RdpSessionState.Active;
            _logger.LogInformation(
                "RDP session reconnected for VM {VmName}, token={TokenPrefix}...",
                session.VmName,
                token[..8]
            );
            return session;
        }

        _logger.LogWarning(
            "RDP session token {TokenPrefix}... is in state {State}, rejecting",
            token[..8],
            session.State
        );
        return null;
    }

    public void CompleteSession(string token)
    {
        if (_sessions.TryGetValue(token, out RdpSession? session))
        {
            session.State = RdpSessionState.Completed;
            session.CompletedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "RDP session completed for VM {VmName}, token={TokenPrefix}... (reconnect allowed for 30s)",
                session.VmName,
                token[..8]
            );
        }
    }

    public void ForceDisconnect(string token)
    {
        if (_sessions.TryGetValue(token, out RdpSession? session))
        {
            session.Cancellation.Cancel();
            session.State = RdpSessionState.Completed;
            session.CompletedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "RDP session force-disconnected for VM {VmName}, token={TokenPrefix}...",
                session.VmName,
                token[..8]
            );
        }
    }

    public IReadOnlyList<RdpSession> GetAllSessions()
    {
        return _sessions.Values.ToList();
    }

    public void DisconnectSessionsForUser(string vmName, string username)
    {
        foreach (KeyValuePair<string, RdpSession> kvp in _sessions)
        {
            RdpSession session = kvp.Value;
            if (session.State != RdpSessionState.Active)
                continue;
            if (!string.Equals(session.VmName, vmName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase))
                continue;

            _logger.LogInformation(
                "Disconnecting RDP session for {Username} on {VmName} (share revoked)",
                username,
                vmName
            );
            session.Cancellation.Cancel();
            session.State = RdpSessionState.Completed;
            session.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    private void CleanupExpired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<string> toRemove = new List<string>();

        foreach (KeyValuePair<string, RdpSession> kvp in _sessions)
        {
            if (
                kvp.Value.State == RdpSessionState.Pending
                && now - kvp.Value.CreatedAt > PendingExpiry
            )
                toRemove.Add(kvp.Key);
            else if (
                kvp.Value.State == RdpSessionState.Completed
                && now - kvp.Value.CreatedAt > CompletedRetention
            )
                toRemove.Add(kvp.Key);
        }

        foreach (string key in toRemove)
            _sessions.TryRemove(key, out _);

        if (toRemove.Count > 0)
            _logger.LogDebug("Cleaned up {Count} expired RDP sessions", toRemove.Count);
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

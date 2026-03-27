using Microsoft.Extensions.Logging;

namespace VmManager.Services;

public sealed class AgentConnection : IDisposable
{
    private const int LocalPort = 18275;
    private const int HealthCheckMaxRetries = 30;
    private const int HealthCheckIntervalMs = 500;

    private readonly ILogger<AgentConnection> _logger;
    private Thread? _agentThread;
    private CancellationTokenSource? _agentCts;
    private AgentClient? _localClient;
    private AgentClient? _client;
    private Exception? _agentStartupError;

    public AgentConnection(ILogger<AgentConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public AgentClient? Client => _client;
    public bool IsLocal { get; private set; }
    public bool IsConnected => _client != null;

    public async Task ConnectLocalAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local agent requires Windows");

        if (_agentThread == null || !_agentThread.IsAlive)
        {
            _agentCts = new CancellationTokenSource();
            CancellationToken token = _agentCts.Token;

            _agentStartupError = null;
            _agentThread = new Thread(() => StartAgentHost(token))
            {
                IsBackground = true,
                Name = "VmManager-Agent",
            };
            _agentThread.Start();
            _logger.LogInformation(
                "Agent thread started, thread alive: {IsAlive}",
                _agentThread.IsAlive
            );
        }

        if (_localClient == null)
        {
            string localUrl = "http://localhost:" + LocalPort;
            AgentClient tempClient = new AgentClient(
                localUrl,
                _logger as ILogger<AgentClient>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentClient>.Instance
            );

            _logger.LogInformation("Waiting for local agent to become ready");

            for (int i = 0; i < HealthCheckMaxRetries; i++)
            {
                if (_agentStartupError != null)
                    throw new InvalidOperationException(
                        "Local agent failed to start: " + _agentStartupError.Message,
                        _agentStartupError
                    );

                if (!_agentThread.IsAlive)
                    throw new InvalidOperationException("Local agent thread exited unexpectedly");

                bool healthy = false;
                try
                {
                    healthy = await tempClient.IsHealthyAsync();
                }
                catch (Exception healthEx)
                {
                    _logger.LogWarning(
                        "Health check attempt {Attempt} failed: {Error}",
                        i + 1,
                        healthEx.Message
                    );
                }

                if (healthy)
                {
                    _logger.LogInformation(
                        "Local agent is ready at http://localhost:{Port}",
                        LocalPort
                    );
                    break;
                }

                _logger.LogInformation(
                    "Health check attempt {Attempt}/{Max}, thread alive: {IsAlive}",
                    i + 1,
                    HealthCheckMaxRetries,
                    _agentThread.IsAlive
                );
                await Task.Delay(HealthCheckIntervalMs);

                if (i == HealthCheckMaxRetries - 1)
                    throw new TimeoutException(
                        "Local agent did not become ready within "
                            + (HealthCheckMaxRetries * HealthCheckIntervalMs / 1000)
                            + " seconds"
                    );
            }

            _localClient = tempClient;
        }

        _client = _localClient;
        IsLocal = true;
    }

    public async Task ConnectRemoteAsync(
        string url,
        string? username = null,
        string? password = null,
        string? rdpProxyHost = null
    )
    {
        AgentClient client = new AgentClient(
            url,
            _logger as ILogger<AgentClient>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentClient>.Instance,
            username,
            password,
            rdpProxyHost
        );

        if (!await client.IsHealthyAsync())
            throw new InvalidOperationException("Remote agent at " + url + " is not reachable");

        _client = client;
        IsLocal = false;
        _logger.LogInformation("Connected to remote agent at {Url}", url);
    }

    public void SwitchToLocal()
    {
        if (_localClient == null)
            throw new InvalidOperationException("Local agent is not available");
        _client = _localClient;
        IsLocal = true;
        _logger.LogInformation("Switched back to local agent");
    }

    private void StartAgentHost(CancellationToken cancellationToken)
    {
        try
        {
#if WINDOWS && !CLIENT_ONLY
            string[] args = Array.Empty<string>();
            Agent.AgentHost.RunAsync(args, cancellationToken).GetAwaiter().GetResult();
#else
            throw new PlatformNotSupportedException("Local agent not available in this build");
#endif
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _agentStartupError = ex;
            _logger.LogError(ex, "Local agent failed to start");
        }
    }

    public void Dispose()
    {
        _client = null;
        _localClient?.Dispose();
        _localClient = null;

        if (_agentCts != null)
        {
            _agentCts.Cancel();
            _agentThread?.Join(TimeSpan.FromSeconds(5));
            _agentCts.Dispose();
            _agentCts = null;
            _agentThread = null;
        }
    }
}

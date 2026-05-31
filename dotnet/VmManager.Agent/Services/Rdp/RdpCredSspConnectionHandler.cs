using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using VmManager.Contracts.Models;

namespace VmManager.Agent.Services.Rdp;

public sealed class RdpCredSspConnectionHandler
{
    private readonly ILogger<RdpCredSspConnectionHandler> _logger;
    private readonly ClientCredSspHandler _clientHandler;
    private readonly VmCredSspHandler _vmHandler;
    private readonly CertificateFactory _certificateFactory;
    private readonly UserService _userService;
    private readonly AuthorizationService _authorizationService;
    private readonly IVmIpResolver _vmIpResolver;
    private readonly RdpSessionStore _sessionStore;
    private readonly RdpTcpRelay _relay;
    private readonly SettingsService _settingsService;

    public RdpCredSspConnectionHandler(
        ILogger<RdpCredSspConnectionHandler> logger,
        ClientCredSspHandler clientHandler,
        VmCredSspHandler vmHandler,
        CertificateFactory certificateFactory,
        UserService userService,
        AuthorizationService authorizationService,
        IVmIpResolver vmIpResolver,
        RdpSessionStore sessionStore,
        RdpTcpRelay relay,
        SettingsService settingsService
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clientHandler);
        ArgumentNullException.ThrowIfNull(vmHandler);
        ArgumentNullException.ThrowIfNull(certificateFactory);
        ArgumentNullException.ThrowIfNull(userService);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(vmIpResolver);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(settingsService);

        _logger = logger;
        _clientHandler = clientHandler;
        _vmHandler = vmHandler;
        _certificateFactory = certificateFactory;
        _userService = userService;
        _authorizationService = authorizationService;
        _vmIpResolver = vmIpResolver;
        _sessionStore = sessionStore;
        _relay = relay;
        _settingsService = settingsService;
    }

    public async Task HandleConnectionAsync(
        Stream clientStream,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Read client X.224 Connection Request
            byte[] x224Payload = await X224Handler.ReadPayloadAsync(
                clientStream,
                cancellationToken
            );

            // Send X.224 Confirm with PROTOCOL_HYBRID_EX and full flags
            byte[] confirm = X224Handler.BuildConnectionConfirm(0x08, 0x3F);
            await clientStream.WriteAsync(confirm, cancellationToken);

            // TLS handshake with client (captures SNI hostname)
            X509Certificate2 cert = _certificateFactory.GetCertificate();
            (SslStream clientSsl, string? sniHostname) =
                await _clientHandler.PerformTlsHandshakeAsync(
                    clientStream,
                    cert,
                    cancellationToken
                );

            ClientAuthResult authResult = await _clientHandler.PerformNtlmExchangeAsync(
                clientSsl,
                cert,
                cancellationToken
            );
            authResult.SniHostname = sniHostname;

            (string vmName, string username) = ResolveVmAndUsername(authResult);
            _logger.LogInformation(
                "RDP CredSSP: user={Username}, vm={VmName}, mode={Mode}",
                username,
                vmName,
                sniHostname != null ? "SNI" : "username-prefix"
            );

            byte[]? ntHash = _userService.GetNtHash(username);
            if (ntHash == null)
            {
                _logger.LogWarning(
                    "User {Username} has no NT hash (needs password reset or login)",
                    username
                );
                return;
            }

            if (!_clientHandler.DeriveSessionKey(authResult, ntHash))
            {
                _logger.LogWarning("Failed to derive session key for user {Username}", username);
                return;
            }

            // Send CredSSP v6 pubKeyAuth response
            await _clientHandler.SendPubKeyResponseAsync(
                clientSsl,
                authResult,
                cert,
                cancellationToken
            );

            await _clientHandler.ReadTsCredentialsAsync(clientSsl, cancellationToken);

            UserAccount? user = _userService.GetByUsername(username);
            if (user == null)
            {
                _logger.LogWarning("User {Username} not found", username);
                return;
            }

            ClaimsPrincipal principal = BuildClaimsPrincipal(user);

            if (!_authorizationService.HasPermission(principal, Permission.RdpConnect))
            {
                _logger.LogWarning(
                    "User {Username} does not have rdp.connect permission",
                    username
                );
                return;
            }

            if (!_authorizationService.CanAccessVm(principal, vmName, Permission.RdpConnect))
            {
                _logger.LogWarning("User {Username} cannot access VM {VmName}", username, vmName);
                return;
            }

            string? vmIp = await _vmIpResolver.ResolveIpAsync(vmName, cancellationToken);
            if (string.IsNullOrEmpty(vmIp))
            {
                _logger.LogWarning("Cannot resolve IP for VM {VmName}", vmName);
                return;
            }

            // Connect to VM and do X.224
            AppSettings settings = _settingsService.Load();
            (TcpClient vmTcp, NetworkStream vmNet, int vmProtocol, byte vmNegFlags) =
                await _vmHandler.ConnectAndNegotiateX224Async(vmIp, 3389, cancellationToken);

            using (vmTcp)
            {
                // VM TLS + CredSSP
                string vmDomain = "";
                (SslStream vmSsl, NegotiateAuthentication nego) =
                    await _vmHandler.AuthenticateAsync(
                        vmNet,
                        vmIp,
                        settings.DefaultVmUsername,
                        settings.DefaultVmPassword,
                        vmDomain,
                        cancellationToken
                    );

                await _vmHandler.HandleEarlyAuthResultAsync(
                    clientSsl,
                    vmSsl,
                    vmNegFlags,
                    cancellationToken
                );

                RdpSession session = _sessionStore.CreateSession(vmName, vmIp, username);
                session.State = RdpSessionState.Active;

                try
                {
                    _logger.LogInformation(
                        "RDP relay started: {Username} -> {VmName} ({VmIp})",
                        username,
                        vmName,
                        vmIp
                    );

                    string connectionId = vmName + "-" + Guid.NewGuid().ToString("N")[..8];
                    await _relay.RelayAsync(clientSsl, vmSsl, connectionId, cancellationToken);
                }
                finally
                {
                    _sessionStore.CompleteSession(session.Token);
                    _logger.LogInformation(
                        "RDP session ended: {Username} on {VmName}",
                        username,
                        vmName
                    );
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug("RDP connection closed: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RDP CredSSP connection error");
        }
    }

    private (string VmName, string Username) ResolveVmAndUsername(ClientAuthResult authResult)
    {
        AppSettings settings = _settingsService.Load();

        // Mode 1: SNI hostname (e.g. myVm.lab.myDomain.com)
        if (
            !string.IsNullOrEmpty(authResult.SniHostname)
            && !string.IsNullOrEmpty(settings.RdpDomainSuffix)
        )
        {
            string suffix = "." + settings.RdpDomainSuffix.TrimStart('.');
            if (authResult.SniHostname.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string vmName = authResult.SniHostname[..^suffix.Length];
                return (vmName, authResult.Username);
            }
        }

        // Mode 2: Username prefix (e.g. myVm:meow@purr.de)
        int colonIndex = authResult.Username.IndexOf(':');
        if (colonIndex > 0)
        {
            string vmName = authResult.Username[..colonIndex];
            string username = authResult.Username[(colonIndex + 1)..];
            return (vmName, username);
        }

        throw new InvalidOperationException(
            "Cannot determine VM name. Use vmName:username format or configure RdpDomainSuffix for DNS mode."
        );
    }

    private static ClaimsPrincipal BuildClaimsPrincipal(UserAccount user)
    {
        List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, user.Username) };

        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        foreach (string permission in user.Permissions)
            claims.Add(new Claim(Permission.PermissionClaimType, permission));

        ClaimsIdentity identity = new ClaimsIdentity(claims, "RdpCredSsp");
        return new ClaimsPrincipal(identity);
    }
}

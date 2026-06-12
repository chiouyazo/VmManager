using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using VmManager.Agent.Services.Monitoring;
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
    private readonly LoginAttemptTracker _loginAttemptTracker;
    private readonly IVmTrackingService _vmTrackingService;
    private readonly VmCredentialStore _vmCredentialStore;

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
        SettingsService settingsService,
        LoginAttemptTracker loginAttemptTracker,
        IVmTrackingService vmTrackingService,
        VmCredentialStore vmCredentialStore
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
        ArgumentNullException.ThrowIfNull(vmTrackingService);
        ArgumentNullException.ThrowIfNull(vmCredentialStore);

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
        _loginAttemptTracker = loginAttemptTracker;
        _vmTrackingService = vmTrackingService;
        _vmCredentialStore = vmCredentialStore;
    }

    public async Task HandleConnectionAsync(
        Stream clientStream,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Read client X.224 Connection Request
            _logger.LogDebug("Reading client X.224 Connection Request...");
            byte[] x224Payload = await X224Handler.ReadPayloadAsync(
                clientStream,
                cancellationToken
            );
            _logger.LogDebug(
                "Client X.224 payload: {Length} bytes, hex={Hex}",
                x224Payload.Length,
                Convert.ToHexString(x224Payload, 0, Math.Min(32, x224Payload.Length))
            );

            // Send X.224 Confirm with PROTOCOL_HYBRID_EX and full flags
            byte[] confirm = X224Handler.BuildConnectionConfirm(0x08, 0x3F);
            await clientStream.WriteAsync(confirm, cancellationToken);
            _logger.LogDebug("Sent X.224 Confirm (HYBRID_EX, flags=0x3F)");

            // TLS handshake with client (captures SNI hostname)
            X509Certificate2 cert = _certificateFactory.GetCertificate();
            _logger.LogDebug("Starting TLS handshake with client...");
            (SslStream clientSsl, string? sniHostname) =
                await _clientHandler.PerformTlsHandshakeAsync(
                    clientStream,
                    cert,
                    cancellationToken
                );
            _logger.LogDebug(
                "TLS handshake complete: SNI={Sni}, Protocol={Protocol}, Cipher={Cipher}",
                sniHostname,
                clientSsl.SslProtocol,
                clientSsl.CipherAlgorithm
            );

            _logger.LogDebug("Starting NTLM exchange...");
            ClientAuthResult authResult = await _clientHandler.PerformNtlmExchangeAsync(
                clientSsl,
                cert,
                cancellationToken
            );
            authResult.SniHostname = sniHostname;

            string vmName;
            string username;
            try
            {
                (vmName, username) = ResolveVmAndUsername(authResult);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("VM resolution failed: {Message}", ex.Message);
                return;
            }

            _logger.LogInformation(
                "RDP CredSSP: user={Username}, vm={VmName}, ntlmUser={NtlmUser}, ntlmDomain={NtlmDomain}",
                username,
                vmName,
                authResult.Username,
                authResult.Domain
            );

            byte[]? ntHash = _userService.GetNtHash(username);
            if (ntHash == null)
            {
                _logger.LogWarning(
                    "User {Username} has no NT hash (needs password reset or login)",
                    username
                );
                await SendCredSspError(clientSsl, 0x8009030C, cancellationToken);
                return;
            }

            if (!_clientHandler.DeriveSessionKey(authResult, ntHash))
            {
                _logger.LogWarning("Failed to derive session key for user {Username}", username);
                await SendCredSspError(clientSsl, 0x8009030C, cancellationToken);
                return;
            }

            if (!_clientHandler.VerifyClientPubKeyAuth(authResult, authResult.RawCredSspAuth, cert))
            {
                _loginAttemptTracker.RecordFailedAttempt(username);
                _logger.LogWarning(
                    "Invalid credentials for user {Username} (pubKeyAuth verification failed)",
                    username
                );
                await SendCredSspError(clientSsl, 0x8009030C, cancellationToken);
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

            if (user.MustChangePassword)
            {
                _logger.LogWarning(
                    "User {Username} must change password before connecting to VMs",
                    username
                );
                await SendCredSspError(clientSsl, 0x8009030C, cancellationToken);
                return;
            }

            ClaimsPrincipal principal = BuildClaimsPrincipal(user);

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
                (string vmUser, string vmPassword) = ResolveVmCredentials(
                    username,
                    vmName,
                    settings
                );
                string vmDomain = "";
                if (vmUser.Contains('\\'))
                {
                    string[] parts = vmUser.Split('\\', 2);
                    vmDomain = parts[0] == "." ? "" : parts[0];
                    vmUser = parts[1];
                }
                NegotiateAuthentication nego;
                SslStream vmSsl;
                (vmSsl, nego) = await _vmHandler.AuthenticateAsync(
                    vmNet,
                    vmIp,
                    vmUser,
                    vmPassword,
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
                string resolvedUsername = authResult.Username;
                if (
                    _userService.GetByUsername(resolvedUsername) == null
                    && !string.IsNullOrEmpty(authResult.Domain)
                )
                {
                    resolvedUsername = authResult.Domain + "\\" + authResult.Username;
                }
                return (vmName, resolvedUsername);
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

    private static async Task SendCredSspError(
        SslStream clientSsl,
        uint errorCode,
        CancellationToken cancellationToken
    )
    {
        // TSRequest with errorCode [4]: SEQUENCE { [0] version, [4] errorCode }
        byte[] errorBytes = new byte[4];
        errorBytes[0] = (byte)(errorCode >> 24);
        errorBytes[1] = (byte)(errorCode >> 16);
        errorBytes[2] = (byte)(errorCode >> 8);
        errorBytes[3] = (byte)errorCode;

        byte[] version = Crypto.Asn1Helper.Wrap(
            0xA0,
            Crypto.Asn1Helper.Wrap(0x02, new byte[] { 0x06 })
        );
        byte[] error = Crypto.Asn1Helper.Wrap(0xA4, Crypto.Asn1Helper.Wrap(0x02, errorBytes));
        byte[] tsRequest = Crypto.Asn1Helper.Wrap(0x30, Crypto.Asn1Helper.Concat(version, error));

        try
        {
            await clientSsl.WriteAsync(tsRequest, cancellationToken);
        }
        catch
        {
            // Client may have already disconnected
        }
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

    private (string VmUser, string VmPassword) ResolveVmCredentials(
        string username,
        string vmName,
        AppSettings settings
    )
    {
        // Level 1: Per-user-per-VM credentials
        (string? perUserVmUser, string? perUserVmPass) = _vmCredentialStore.GetCredentials(
            vmName,
            username
        );
        if (!string.IsNullOrEmpty(perUserVmUser))
        {
            _logger.LogDebug(
                "Using per-user-per-VM credentials for {User} on {Vm}",
                username,
                vmName
            );
            return (perUserVmUser, perUserVmPass ?? "");
        }

        // Level 2: Per-user global credentials
        UserAccount? account = _userService.GetByUsername(username);
        if (account != null && !string.IsNullOrEmpty(account.VmUsername))
        {
            _logger.LogDebug("Using per-user global credentials for {User}", username);
            return (account.VmUsername, account.VmPassword);
        }

        // Level 3: Per-VM default credentials (set by owner)
        (string? vmDefaultUser, string? vmDefaultPass) = _vmTrackingService.GetVmCredentials(
            vmName
        );
        if (!string.IsNullOrEmpty(vmDefaultUser))
        {
            _logger.LogDebug("Using per-VM default credentials for {Vm}", vmName);
            return (vmDefaultUser, vmDefaultPass ?? "");
        }

        // Level 4: Global default
        _logger.LogDebug("Using global default credentials");
        return (settings.DefaultVmUsername, settings.DefaultVmPassword);
    }
}

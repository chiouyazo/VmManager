using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VmManager.Agent.Services.Rdp.Crypto;

namespace VmManager.Agent.Services.Rdp;

public sealed class ClientCredSspHandler
{
    private readonly ILogger<ClientCredSspHandler> _logger;

    public ClientCredSspHandler(ILogger<ClientCredSspHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<(SslStream Stream, string? SniHostname)> PerformTlsHandshakeAsync(
        Stream clientStream,
        X509Certificate2 certificate,
        CancellationToken cancellationToken
    )
    {
        string? sniHostname = null;

        SslStream sslStream = new SslStream(clientStream, leaveInnerStreamOpen: false);
        SslServerAuthenticationOptions options = new SslServerAuthenticationOptions
        {
            ServerCertificateSelectionCallback = (_, hostName) =>
            {
                sniHostname = hostName;
                return certificate;
            },
        };

        await sslStream.AuthenticateAsServerAsync(options, cancellationToken);
        _logger.LogDebug(
            "Client TLS handshake complete: {Protocol}, SNI={Sni}",
            sslStream.SslProtocol,
            sniHostname
        );

        return (sslStream, sniHostname);
    }

    public async Task<ClientAuthResult> PerformNtlmExchangeAsync(
        SslStream clientSsl,
        X509Certificate2 certificate,
        CancellationToken cancellationToken
    )
    {
        // Read NTLM NEGOTIATE (Type 1) wrapped in CredSSP TSRequest
        byte[] credSspNeg = await X224Handler.ReadAvailableAsync(clientSsl, cancellationToken);
        int negOffset = NtlmType3Parser.FindNtlmssp(credSspNeg);
        if (negOffset < 0)
            throw new InvalidOperationException("No NTLMSSP signature in CredSSP NEGOTIATE");

        // Build and send NTLM CHALLENGE (Type 2)
        byte[] serverChallenge = RandomNumberGenerator.GetBytes(8);
        byte[] type2Message = NtlmType2Builder.Build(serverChallenge);
        byte[] credSspChallenge = CredSspMessageBuilder.WrapNtlmToken(type2Message);
        await clientSsl.WriteAsync(credSspChallenge, cancellationToken);

        // Read NTLM AUTHENTICATE (Type 3) wrapped in CredSSP TSRequest
        byte[] credSspAuth = await X224Handler.ReadAvailableAsync(clientSsl, cancellationToken);
        int authOffset = NtlmType3Parser.FindNtlmssp(credSspAuth);
        if (authOffset < 0)
            throw new InvalidOperationException("No NTLMSSP signature in CredSSP AUTHENTICATE");

        ClientAuthResult authResult = NtlmType3Parser.Parse(credSspAuth, authOffset);
        authResult.ClientNonce = CredSspMessageParser.ExtractNonce(credSspAuth);
        authResult.RawCredSspAuth = credSspAuth;

        _logger.LogInformation("CredSSP client authenticated as: {Username}", authResult.Username);

        return authResult;
    }

    public bool DeriveSessionKey(ClientAuthResult authResult, byte[] ntHash)
    {
        _logger.LogDebug(
            "DeriveSessionKey: username={Username}, domain={Domain}, ntHash={NtHash}",
            authResult.Username,
            authResult.Domain,
            Convert.ToHexString(ntHash)
        );

        byte[] ntv2Hash = NtlmCrypto.ComputeNtv2Hash(
            ntHash,
            authResult.Username,
            authResult.Domain
        );
        byte[] sessionBaseKey = NtlmCrypto.ComputeSessionBaseKey(ntv2Hash, authResult.NtProofStr);

        if (
            authResult.EncryptedRandomSessionKey != null
            && authResult.EncryptedRandomSessionKey.Length == 16
        )
        {
            authResult.ExportedSessionKey = NtlmCrypto.DecryptExportedSessionKey(
                sessionBaseKey,
                authResult.EncryptedRandomSessionKey
            );
        }
        else
        {
            authResult.ExportedSessionKey = sessionBaseKey;
        }

        return true;
    }

    public bool VerifyClientPubKeyAuth(
        ClientAuthResult authResult,
        byte[] credSspAuth,
        X509Certificate2 certificate
    )
    {
        byte[] subjectPublicKey = Asn1Helper.ExtractSubjectPublicKey(
            certificate.PublicKey.ExportSubjectPublicKeyInfo()
        );

        byte[] expectedClientHash = NtlmCrypto.ComputeClientServerHash(
            authResult.ClientNonce ?? Array.Empty<byte>(),
            subjectPublicKey
        );

        byte[]? clientPubKeyAuth = CredSspMessageParser.ExtractPubKeyAuth(credSspAuth, 0);
        if (clientPubKeyAuth == null)
        {
            _logger.LogWarning("No pubKeyAuth in client TSRequest");
            return false;
        }

        _logger.LogInformation(
            "Client pubKeyAuth: {Length} bytes (should be 48 = 16 sig + 32 encrypted)",
            clientPubKeyAuth.Length
        );

        byte[] decrypted = NtlmCrypto.Unseal(
            authResult.ExportedSessionKey,
            clientPubKeyAuth,
            clientToServer: true
        );

        bool match = decrypted.SequenceEqual(expectedClientHash);
        _logger.LogInformation("Client pubKeyAuth verification: {Match}", match);
        if (!match)
        {
            _logger.LogWarning(
                "Session key mismatch. Decrypted={Decrypted}, Expected={Expected}",
                Convert.ToHexString(decrypted).Substring(0, Math.Min(20, decrypted.Length * 2)),
                Convert
                    .ToHexString(expectedClientHash)
                    .Substring(0, Math.Min(20, expectedClientHash.Length * 2))
            );
        }

        return match;
    }

    public async Task SendPubKeyResponseAsync(
        SslStream clientSsl,
        ClientAuthResult authResult,
        X509Certificate2 certificate,
        CancellationToken cancellationToken
    )
    {
        byte[] subjectPublicKey = Asn1Helper.ExtractSubjectPublicKey(
            certificate.PublicKey.ExportSubjectPublicKeyInfo()
        );

        byte[] serverClientHash = NtlmCrypto.ComputeServerClientHash(
            authResult.ClientNonce ?? Array.Empty<byte>(),
            subjectPublicKey
        );

        byte[] sealedHash = NtlmCrypto.Seal(
            authResult.ExportedSessionKey,
            serverClientHash,
            serverToClient: true
        );

        byte[] response = CredSspMessageBuilder.BuildPubKeyResponse(
            sealedHash,
            authResult.ClientNonce
        );
        await clientSsl.WriteAsync(response, cancellationToken);
    }

    public async Task ReadTsCredentialsAsync(
        SslStream clientSsl,
        CancellationToken cancellationToken
    )
    {
        await X224Handler.ReadAvailableAsync(clientSsl, cancellationToken);
    }
}

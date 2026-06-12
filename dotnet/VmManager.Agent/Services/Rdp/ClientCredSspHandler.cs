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
        _logger.LogDebug("Waiting for client NTLM Type 1 (NEGOTIATE)...");
        byte[] credSspNeg = await X224Handler.ReadAvailableAsync(clientSsl, cancellationToken);
        _logger.LogDebug(
            "Received CredSSP NEGOTIATE: {Length} bytes, hex={Hex}",
            credSspNeg.Length,
            Convert.ToHexString(credSspNeg, 0, Math.Min(64, credSspNeg.Length))
        );

        int negOffset = NtlmType3Parser.FindNtlmssp(credSspNeg);
        if (negOffset < 0)
        {
            _logger.LogWarning(
                "No NTLMSSP signature found in {Length} bytes: {Hex}",
                credSspNeg.Length,
                Convert.ToHexString(credSspNeg, 0, Math.Min(128, credSspNeg.Length))
            );
            throw new InvalidOperationException("No NTLMSSP signature in CredSSP NEGOTIATE");
        }

        int clientVersion = CredSspMessageParser.ExtractVersion(credSspNeg);
        int negotiatedVersion = Math.Min(clientVersion, 6);
        _logger.LogDebug(
            "NTLM Type 1 found at offset {Offset}, CredSSP clientVersion={ClientVersion}, negotiated={Negotiated}, Type1 flags=0x{Flags:X8}",
            negOffset,
            clientVersion,
            negotiatedVersion,
            credSspNeg.Length > negOffset + 15
                ? BitConverter.ToUInt32(credSspNeg, negOffset + 12)
                : 0
        );

        // Build and send NTLM CHALLENGE (Type 2)
        byte[] serverChallenge = RandomNumberGenerator.GetBytes(8);
        byte[] type2Message = NtlmType2Builder.Build(serverChallenge);
        byte[] credSspChallenge = CredSspMessageBuilder.WrapNtlmToken(
            type2Message,
            negotiatedVersion
        );
        _logger.LogDebug(
            "Sending CredSSP CHALLENGE: {Length} bytes, Type2={Type2Len} bytes",
            credSspChallenge.Length,
            type2Message.Length
        );
        await clientSsl.WriteAsync(credSspChallenge, cancellationToken);

        // Read NTLM AUTHENTICATE (Type 3) wrapped in CredSSP TSRequest
        _logger.LogDebug("Waiting for client NTLM Type 3 (AUTHENTICATE)...");
        byte[] credSspAuth = await X224Handler.ReadAvailableAsync(clientSsl, cancellationToken);
        _logger.LogDebug(
            "Received CredSSP AUTHENTICATE: {Length} bytes, hex={Hex}",
            credSspAuth.Length,
            Convert.ToHexString(credSspAuth, 0, Math.Min(64, credSspAuth.Length))
        );

        int authOffset = NtlmType3Parser.FindNtlmssp(credSspAuth);
        if (authOffset < 0)
        {
            _logger.LogWarning(
                "No NTLMSSP signature in AUTHENTICATE: {Hex}",
                Convert.ToHexString(credSspAuth, 0, Math.Min(128, credSspAuth.Length))
            );
            throw new InvalidOperationException("No NTLMSSP signature in CredSSP AUTHENTICATE");
        }

        ClientAuthResult authResult = NtlmType3Parser.Parse(credSspAuth, authOffset);
        authResult.CredSspVersion = negotiatedVersion;
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

        byte[]? clientPubKeyAuth = CredSspMessageParser.ExtractPubKeyAuth(credSspAuth, 0);
        if (clientPubKeyAuth == null)
        {
            _logger.LogWarning("No pubKeyAuth in client TSRequest");
            return false;
        }

        byte[] decrypted = NtlmCrypto.Unseal(
            authResult.ExportedSessionKey,
            clientPubKeyAuth,
            clientToServer: true
        );

        bool match;
        if (authResult.CredSspVersion >= 5)
        {
            byte[] expectedHash = NtlmCrypto.ComputeClientServerHash(
                authResult.ClientNonce ?? Array.Empty<byte>(),
                subjectPublicKey
            );
            match = decrypted.SequenceEqual(expectedHash);
        }
        else
        {
            match = decrypted.SequenceEqual(subjectPublicKey);
        }

        _logger.LogDebug(
            "pubKeyAuth verification (v{Version}): {Match}",
            authResult.CredSspVersion,
            match
        );
        if (!match)
        {
            _logger.LogWarning(
                "pubKeyAuth mismatch. Decrypted={Decrypted}, ExpectedLen={ExpectedLen}",
                Convert.ToHexString(decrypted, 0, Math.Min(10, decrypted.Length)),
                authResult.CredSspVersion >= 5 ? 32 : subjectPublicKey.Length
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

        byte[] dataToSeal;
        if (authResult.CredSspVersion >= 5)
        {
            dataToSeal = NtlmCrypto.ComputeServerClientHash(
                authResult.ClientNonce ?? Array.Empty<byte>(),
                subjectPublicKey
            );
        }
        else
        {
            dataToSeal = (byte[])subjectPublicKey.Clone();
            dataToSeal[0] = (byte)(dataToSeal[0] + 1);
        }

        byte[] sealedData = NtlmCrypto.Seal(
            authResult.ExportedSessionKey,
            dataToSeal,
            serverToClient: true
        );

        byte[] response = CredSspMessageBuilder.BuildPubKeyResponse(
            sealedData,
            authResult.ClientNonce,
            authResult.CredSspVersion
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

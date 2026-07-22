using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VmManager.Agent.Services.Rdp.Crypto;

namespace VmManager.Agent.Services.Rdp;

public sealed class VmCredSspHandler
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<VmCredSspHandler> _logger;

    public VmCredSspHandler(ILogger<VmCredSspHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<(
        TcpClient TcpClient,
        NetworkStream NetStream,
        int SelectedProtocol,
        byte NegFlags
    )> ConnectAndNegotiateX224Async(string vmIp, int vmPort, CancellationToken cancellationToken)
    {
        // Bound the whole connect + X.224 negotiation: an unreachable or still-booting VM must
        // fail fast instead of hanging the client until the OS connect timeout (~21s) or longer.
        using CancellationTokenSource opCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        opCts.CancelAfter(ConnectTimeout);
        CancellationToken ct = opCts.Token;

        TcpClient tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(vmIp, vmPort, ct);
            ProxySocketTuning.Apply(tcpClient.Client, _logger);
            NetworkStream netStream = tcpClient.GetStream();

            await netStream.WriteAsync(X224Handler.BuildConnectionRequest(), ct);
            byte[] x224Payload = await X224Handler.ReadPayloadAsync(netStream, ct);
            (int selectedProtocol, byte negFlags) = X224Handler.ParseConfirmResponse(x224Payload);

            _logger.LogDebug(
                "VM X.224 negotiated: protocol=0x{Protocol:X}, flags=0x{Flags:X2}",
                selectedProtocol,
                negFlags
            );

            return (tcpClient, netStream, selectedProtocol, negFlags);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            tcpClient.Dispose();
            throw new TimeoutException(
                $"Timed out connecting to / negotiating with VM at {vmIp}:{vmPort} after {ConnectTimeout.TotalSeconds:0}s"
            );
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    public async Task<(SslStream SslStream, NegotiateAuthentication Auth)> AuthenticateAsync(
        NetworkStream netStream,
        string vmIp,
        string vmUser,
        string vmPassword,
        string vmDomain,
        CancellationToken outerToken
    )
    {
        // Bound the credential exchange so a wedged VM cannot stall the proxy indefinitely.
        // All awaits below observe this local token, which trips on the outer token or the timeout.
        using CancellationTokenSource authCts = CancellationTokenSource.CreateLinkedTokenSource(
            outerToken
        );
        authCts.CancelAfter(AuthTimeout);
        CancellationToken cancellationToken = authCts.Token;

        SslStream vmSsl = new SslStream(netStream, leaveInnerStreamOpen: false);
        await vmSsl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = vmIp,
                // The proxy MITMs the RDP session, so VM RDP certs are self-signed by design.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
            cancellationToken
        );
        _logger.LogDebug("VM TLS handshake complete: {Protocol}", vmSsl.SslProtocol);

        NegotiateAuthentication nego = new NegotiateAuthentication(
            new NegotiateAuthenticationClientOptions
            {
                Credential = new NetworkCredential(vmUser, vmPassword, vmDomain),
                TargetName = "TERMSRV/" + vmIp,
                RequiredProtectionLevel = ProtectionLevel.EncryptAndSign,
                Package = "NTLM",
            }
        );

        // Step 1: Send SPNEGO NEGOTIATE
        byte[]? token1 = nego.GetOutgoingBlob(
            ReadOnlySpan<byte>.Empty,
            out NegotiateAuthenticationStatusCode status1
        );
        if (token1 == null)
            throw new InvalidOperationException("SPNEGO NEGOTIATE failed: " + status1);

        await vmSsl.WriteAsync(CredSspMessageBuilder.WrapNtlmToken(token1, 6), cancellationToken);

        // Step 2: Read NTLM CHALLENGE
        byte[] challengeResponse = await X224Handler.ReadAvailableAsync(vmSsl, cancellationToken);
        byte[] ntlmChallenge = CredSspMessageParser.ExtractNegoToken(challengeResponse);

        // Step 3: Process challenge, get auth token
        byte[]? authToken = nego.GetOutgoingBlob(
            ntlmChallenge,
            out NegotiateAuthenticationStatusCode status3
        );
        if (authToken == null)
            throw new InvalidOperationException("NTLM AUTHENTICATE failed: " + status3);

        _logger.LogDebug(
            "NTLM auth token: {Length} bytes, status={Status}",
            authToken.Length,
            status3
        );

        // On Linux (gss-ntlmssp), auth may need extra round trips before reaching Completed
        if (status3 == NegotiateAuthenticationStatusCode.ContinueNeeded)
        {
            // Send the auth token without pubKeyAuth first
            await vmSsl.WriteAsync(
                CredSspMessageBuilder.WrapNtlmToken(authToken, 6),
                cancellationToken
            );

            // Read VM's response and process additional legs until Completed
            while (status3 == NegotiateAuthenticationStatusCode.ContinueNeeded)
            {
                byte[] legResponse = await X224Handler.ReadAvailableAsync(vmSsl, cancellationToken);
                byte[] legToken = CredSspMessageParser.ExtractNegoToken(legResponse);
                authToken = nego.GetOutgoingBlob(legToken, out status3);
                _logger.LogDebug("SPNEGO additional leg: status={Status}", status3);

                if (
                    authToken != null
                    && status3 == NegotiateAuthenticationStatusCode.ContinueNeeded
                )
                    await vmSsl.WriteAsync(
                        CredSspMessageBuilder.WrapNtlmToken(authToken, 6),
                        cancellationToken
                    );
            }
        }

        if (status3 != NegotiateAuthenticationStatusCode.Completed)
            throw new InvalidOperationException("SPNEGO auth failed with status: " + status3);

        // Step 4: Compute pubKeyAuth binding
        X509Certificate2 vmCert = new X509Certificate2(
            vmSsl.RemoteCertificate!.Export(X509ContentType.Cert)
        );
        byte[] vmSubjectPubKey = Asn1Helper.ExtractSubjectPublicKey(
            vmCert.PublicKey.ExportSubjectPublicKeyInfo()
        );

        byte[] vmNonce = RandomNumberGenerator.GetBytes(32);
        byte[] vmClientHash = NtlmCrypto.ComputeClientServerHash(vmNonce, vmSubjectPubKey);

        ArrayBufferWriter<byte> pubKeyBuf = new ArrayBufferWriter<byte>();
        NegotiateAuthenticationStatusCode wrapStatus = nego.Wrap(
            vmClientHash,
            pubKeyBuf,
            true,
            out _
        );
        if (wrapStatus != NegotiateAuthenticationStatusCode.Completed)
            throw new InvalidOperationException("Wrap pubKeyAuth failed: " + wrapStatus);

        byte[] sealedPubKeyAuth = pubKeyBuf.WrittenSpan.ToArray();

        // Step 5: Build TSRequest with pubKeyAuth + nonce (and final authToken if present)
        byte[] tsRequest;
        if (authToken != null && authToken.Length > 0)
        {
            tsRequest = CredSspMessageBuilder.BuildAuthenticateRequest(
                authToken,
                sealedPubKeyAuth,
                vmNonce
            );
        }
        else
        {
            tsRequest = CredSspMessageBuilder.BuildPubKeyResponse(sealedPubKeyAuth, vmNonce, 6);
        }
        await vmSsl.WriteAsync(tsRequest, cancellationToken);

        // Step 6: Read VM's pubKeyAuth response
        byte[] vmResponse = await X224Handler.ReadAvailableAsync(vmSsl, cancellationToken);
        if (CredSspMessageParser.HasErrorCode(vmResponse, out uint errorCode))
            throw new InvalidOperationException(
                "VM rejected CredSSP authentication with error 0x" + errorCode.ToString("X8")
            );

        _logger.LogDebug("VM accepted CredSSP authentication");

        // Step 7: Send TSCredentials
        byte[] tsCredentials = CredSspMessageBuilder.BuildTsCredentials(
            vmUser,
            vmPassword,
            vmDomain
        );
        ArrayBufferWriter<byte> credsBuf = new ArrayBufferWriter<byte>();
        nego.Wrap(tsCredentials, credsBuf, true, out _);
        byte[] credsTsRequest = CredSspMessageBuilder.BuildTsCredentialsTsRequest(
            credsBuf.WrittenSpan.ToArray()
        );
        await vmSsl.WriteAsync(credsTsRequest, cancellationToken);

        _logger.LogDebug("VM CredSSP credentials sent");

        return (vmSsl, nego);
    }

    public async Task HandleEarlyAuthResultAsync(
        SslStream clientSsl,
        SslStream vmSsl,
        byte vmNegFlags,
        CancellationToken cancellationToken
    )
    {
        bool vmSendsEarlyAuth = (vmNegFlags & 0x08) != 0 || (vmNegFlags & 0x10) != 0;
        if (!vmSendsEarlyAuth)
        {
            _logger.LogDebug(
                "VM does not send Early User Auth Result (flags=0x{Flags:X2})",
                vmNegFlags
            );
            return;
        }

        byte[] earlyAuthResult = new byte[4];
        await X224Handler.ReadExactAsync(vmSsl, earlyAuthResult, cancellationToken);
        uint authResult = BitConverter.ToUInt32(earlyAuthResult);

        if (authResult != 0)
            throw new InvalidOperationException(
                "VM denied access: Early User Auth Result 0x" + authResult.ToString("X8")
            );

        _logger.LogDebug("VM Early User Auth Result: SUCCESS");

        // Forward to client (client expects it since we forwarded matching flags)
        await clientSsl.WriteAsync(earlyAuthResult, cancellationToken);
    }
}

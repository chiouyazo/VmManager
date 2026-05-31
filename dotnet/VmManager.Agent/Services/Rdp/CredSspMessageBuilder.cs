using System.Text;
using VmManager.Agent.Services.Rdp.Crypto;

namespace VmManager.Agent.Services.Rdp;

public static class CredSspMessageBuilder
{
    public static byte[] WrapNtlmToken(byte[] ntlmMessage)
    {
        byte[] octetString = Asn1Helper.Wrap(0x04, ntlmMessage);
        byte[] context0 = Asn1Helper.Wrap(0xA0, octetString);
        byte[] innerSeq = Asn1Helper.Wrap(0x30, context0);
        byte[] outerSeq = Asn1Helper.Wrap(0x30, innerSeq);
        byte[] negoTokens = Asn1Helper.Wrap(0xA1, outerSeq);
        byte[] version = Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x02, new byte[] { 0x06 }));
        return Asn1Helper.Wrap(0x30, Asn1Helper.Concat(version, negoTokens));
    }

    public static byte[] BuildPubKeyResponse(byte[] sealedHash, byte[]? nonce)
    {
        byte[] version = Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x02, new byte[] { 0x06 }));
        byte[] pubKeyAuth = Asn1Helper.Wrap(0xA3, Asn1Helper.Wrap(0x04, sealedHash));
        byte[] nonceField =
            nonce != null
                ? Asn1Helper.Wrap(0xA5, Asn1Helper.Wrap(0x04, nonce))
                : Array.Empty<byte>();

        byte[] content = Asn1Helper.Concat(Asn1Helper.Concat(version, pubKeyAuth), nonceField);
        return Asn1Helper.Wrap(0x30, content);
    }

    public static byte[] BuildAuthenticateRequest(
        byte[] spnegoToken,
        byte[] sealedPubKeyAuth,
        byte[] nonce
    )
    {
        byte[] negoTokens = Asn1Helper.Wrap(
            0xA1,
            Asn1Helper.Wrap(
                0x30,
                Asn1Helper.Wrap(0x30, Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x04, spnegoToken)))
            )
        );
        byte[] version = Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x02, new byte[] { 0x06 }));
        byte[] pubKeyAuth = Asn1Helper.Wrap(0xA3, Asn1Helper.Wrap(0x04, sealedPubKeyAuth));
        byte[] nonceField = Asn1Helper.Wrap(0xA5, Asn1Helper.Wrap(0x04, nonce));

        byte[] content = Asn1Helper.Concat(
            Asn1Helper.Concat(Asn1Helper.Concat(version, negoTokens), pubKeyAuth),
            nonceField
        );
        return Asn1Helper.Wrap(0x30, content);
    }

    public static byte[] BuildTsCredentials(string username, string password, string domain)
    {
        byte[] domainBytes = Encoding.Unicode.GetBytes(domain);
        byte[] userBytes = Encoding.Unicode.GetBytes(username);
        byte[] passBytes = Encoding.Unicode.GetBytes(password);

        byte[] tsPasswordCreds = Asn1Helper.Wrap(
            0x30,
            Asn1Helper.Concat(
                Asn1Helper.Concat(
                    Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x04, domainBytes)),
                    Asn1Helper.Wrap(0xA1, Asn1Helper.Wrap(0x04, userBytes))
                ),
                Asn1Helper.Wrap(0xA2, Asn1Helper.Wrap(0x04, passBytes))
            )
        );

        byte[] tsCredentials = Asn1Helper.Wrap(
            0x30,
            Asn1Helper.Concat(
                Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x02, new byte[] { 0x01 })),
                Asn1Helper.Wrap(0xA1, Asn1Helper.Wrap(0x04, tsPasswordCreds))
            )
        );

        return tsCredentials;
    }

    public static byte[] BuildTsCredentialsTsRequest(byte[] sealedCredentials)
    {
        return Asn1Helper.Wrap(
            0x30,
            Asn1Helper.Concat(
                Asn1Helper.Wrap(0xA0, Asn1Helper.Wrap(0x02, new byte[] { 0x06 })),
                Asn1Helper.Wrap(0xA2, Asn1Helper.Wrap(0x04, sealedCredentials))
            )
        );
    }
}

using VmManager.Agent.Services.Rdp.Crypto;

namespace VmManager.Agent.Services.Rdp;

public static class CredSspMessageParser
{
    public static byte[] ExtractNegoToken(byte[] tsRequest)
    {
        (int seqContent, _) = Asn1Helper.ParseTagLength(tsRequest, 0);
        (int versionContent, int versionLen) = Asn1Helper.ParseTagLength(tsRequest, seqContent);
        int afterVersion = versionContent + versionLen;

        (int p1, _) = Asn1Helper.ParseTagLength(tsRequest, afterVersion);
        (int p2, _) = Asn1Helper.ParseTagLength(tsRequest, p1);
        (int p3, _) = Asn1Helper.ParseTagLength(tsRequest, p2);
        (int p4, _) = Asn1Helper.ParseTagLength(tsRequest, p3);
        (int p5, int tokenLen) = Asn1Helper.ParseTagLength(tsRequest, p4);

        return tsRequest[p5..(p5 + tokenLen)];
    }

    public static byte[]? ExtractNonce(byte[] credSsp)
    {
        for (int i = 0; i < credSsp.Length - 34; i++)
        {
            if (credSsp[i] != 0xA5)
                continue;

            int lenBytes = 1;
            int contentLen = credSsp[i + 1];
            if (contentLen == 0x81)
            {
                contentLen = credSsp[i + 2];
                lenBytes = 2;
            }
            else if (contentLen == 0x82)
            {
                contentLen = (credSsp[i + 2] << 8) | credSsp[i + 3];
                lenBytes = 3;
            }

            int octetStart = i + 1 + lenBytes;
            if (octetStart >= credSsp.Length || credSsp[octetStart] != 0x04)
                continue;

            int nonceLen = credSsp[octetStart + 1];
            if (nonceLen != 32 || octetStart + 2 + 32 > credSsp.Length)
                continue;

            byte[] nonce = new byte[32];
            Array.Copy(credSsp, octetStart + 2, nonce, 0, 32);
            return nonce;
        }

        return null;
    }

    public static byte[]? ExtractPubKeyAuth(byte[] credSsp, int ntlmOffset)
    {
        // pubKeyAuth is in context tag [3] (0xA3) at top level of TSRequest
        // After the negoTokens [1] field
        (int seqContent, _) = Asn1Helper.ParseTagLength(credSsp, 0);
        int pos = seqContent;

        while (pos < credSsp.Length && credSsp[pos] >= 0xA0)
        {
            byte tag = credSsp[pos];
            (int fieldContent, int fieldLen) = Asn1Helper.ParseTagLength(credSsp, pos);

            if (tag == 0xA3)
            {
                // Inside: OCTET STRING containing the sealed pubKeyAuth
                (int octetContent, int octetLen) = Asn1Helper.ParseTagLength(credSsp, fieldContent);
                byte[] pubKeyAuth = new byte[octetLen];
                Array.Copy(credSsp, octetContent, pubKeyAuth, 0, octetLen);
                return pubKeyAuth;
            }

            pos = fieldContent + fieldLen;
        }

        return null;
    }

    public static bool HasErrorCode(byte[] tsRequest, out uint errorCode)
    {
        errorCode = 0;

        try
        {
            (int seqContent, _) = Asn1Helper.ParseTagLength(tsRequest, 0);
            int pos = seqContent;

            while (pos < tsRequest.Length && tsRequest[pos] >= 0xA0)
            {
                byte tag = tsRequest[pos];
                (int fieldContent, int fieldLen) = Asn1Helper.ParseTagLength(tsRequest, pos);

                if (tag == 0xA4)
                {
                    int intOffset = fieldContent + 2;
                    if (intOffset + 4 <= tsRequest.Length)
                    {
                        errorCode = (uint)(
                            (tsRequest[intOffset] << 24)
                            | (tsRequest[intOffset + 1] << 16)
                            | (tsRequest[intOffset + 2] << 8)
                            | tsRequest[intOffset + 3]
                        );
                    }
                    return true;
                }

                pos = fieldContent + fieldLen;
            }
        }
        catch
        {
            // Malformed ASN.1
        }

        return false;
    }
}

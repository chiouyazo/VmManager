using System.Text;

namespace VmManager.Agent.Services.Rdp;

public static class NtlmType3Parser
{
    private static readonly byte[] NtlmSignature =
    {
        0x4E,
        0x54,
        0x4C,
        0x4D,
        0x53,
        0x53,
        0x50,
        0x00,
    };

    public static int FindNtlmssp(byte[] data)
    {
        for (int i = 0; i <= data.Length - 8; i++)
        {
            bool match = true;
            for (int j = 0; j < 8; j++)
            {
                if (data[i + j] != NtlmSignature[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    public static string ReadString(byte[] message, int ntlmOffset, int fieldOffset)
    {
        int length =
            message[ntlmOffset + fieldOffset] | (message[ntlmOffset + fieldOffset + 1] << 8);
        int offset =
            message[ntlmOffset + fieldOffset + 4] | (message[ntlmOffset + fieldOffset + 5] << 8);

        if (length == 0)
            return "";

        return Encoding.Unicode.GetString(message, ntlmOffset + offset, length);
    }

    public static byte[] ExtractNtProofStr(byte[] message, int ntlmOffset)
    {
        int ntResponseOffset = BitConverter.ToInt32(message, ntlmOffset + 24);
        byte[] ntProofStr = new byte[16];
        Array.Copy(message, ntlmOffset + ntResponseOffset, ntProofStr, 0, 16);
        return ntProofStr;
    }

    public static byte[]? ExtractEncryptedRandomSessionKey(byte[] message, int ntlmOffset)
    {
        int length = BitConverter.ToInt16(message, ntlmOffset + 52);
        int offset = BitConverter.ToInt32(message, ntlmOffset + 56);

        if (length != 16)
            return null;

        byte[] key = new byte[16];
        Array.Copy(message, ntlmOffset + offset, key, 0, 16);
        return key;
    }

    public static ClientAuthResult Parse(byte[] credSspAuth, int ntlmOffset)
    {
        string domain = ReadString(credSspAuth, ntlmOffset, 28);
        string username = ReadString(credSspAuth, ntlmOffset, 36);
        byte[] ntProofStr = ExtractNtProofStr(credSspAuth, ntlmOffset);
        byte[]? encryptedSessionKey = ExtractEncryptedRandomSessionKey(credSspAuth, ntlmOffset);

        return new ClientAuthResult
        {
            Username = username,
            Domain = domain,
            NtProofStr = ntProofStr,
            EncryptedRandomSessionKey = encryptedSessionKey,
        };
    }
}

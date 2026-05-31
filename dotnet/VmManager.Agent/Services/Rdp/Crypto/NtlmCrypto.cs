using System.Security.Cryptography;
using System.Text;

namespace VmManager.Agent.Services.Rdp.Crypto;

public static class NtlmCrypto
{
    public static byte[] ComputeNtv2Hash(byte[] ntHash, string username, string domain)
    {
        byte[] identity = Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
        using HMACMD5 hmac = new HMACMD5(ntHash);
        return hmac.ComputeHash(identity);
    }

    public static byte[] ComputeSessionBaseKey(byte[] ntv2Hash, byte[] ntProofStr)
    {
        using HMACMD5 hmac = new HMACMD5(ntv2Hash);
        return hmac.ComputeHash(ntProofStr);
    }

    public static byte[] DecryptExportedSessionKey(byte[] sessionBaseKey, byte[] encryptedKey)
    {
        return Rc4.Transform(sessionBaseKey, encryptedKey);
    }

    public static byte[] ComputeClientServerHash(byte[] nonce, byte[] subjectPublicKey)
    {
        using MemoryStream stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("CredSSP Client-To-Server Binding Hash\0"));
        stream.Write(nonce);
        stream.Write(subjectPublicKey);
        return SHA256.HashData(stream.ToArray());
    }

    public static byte[] ComputeServerClientHash(byte[] nonce, byte[] subjectPublicKey)
    {
        using MemoryStream stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("CredSSP Server-To-Client Binding Hash\0"));
        stream.Write(nonce);
        stream.Write(subjectPublicKey);
        return SHA256.HashData(stream.ToArray());
    }

    public static byte[] Seal(
        byte[] exportedSessionKey,
        byte[] message,
        bool serverToClient,
        int sequenceNumber = 0
    )
    {
        string sealMagic = serverToClient
            ? "session key to server-to-client sealing key magic constant\0"
            : "session key to client-to-server sealing key magic constant\0";
        string signMagic = serverToClient
            ? "session key to server-to-client signing key magic constant\0"
            : "session key to client-to-server signing key magic constant\0";

        byte[] sealKey = MD5.HashData(
            Concat(exportedSessionKey, Encoding.ASCII.GetBytes(sealMagic))
        );
        byte[] signKey = MD5.HashData(
            Concat(exportedSessionKey, Encoding.ASCII.GetBytes(signMagic))
        );

        Rc4State rc4 = new Rc4State(sealKey);

        byte[] encrypted = rc4.Apply(message);

        byte[] seqBytes = BitConverter.GetBytes(sequenceNumber);
        byte[] hmacInput = Concat(seqBytes, message);
        byte[] hmacResult;
        using (HMACMD5 hmac = new HMACMD5(signKey))
        {
            hmacResult = hmac.ComputeHash(hmacInput);
        }

        byte[] encryptedHmac = rc4.Apply(hmacResult.AsSpan(0, 8).ToArray());

        byte[] signature = new byte[16];
        BitConverter.GetBytes(1).CopyTo(signature, 0);
        Array.Copy(encryptedHmac, 0, signature, 4, 8);
        seqBytes.CopyTo(signature, 12);

        return Concat(signature, encrypted);
    }

    public static byte[] Unseal(
        byte[] exportedSessionKey,
        byte[] sealedMessage,
        bool clientToServer
    )
    {
        string sealMagic = clientToServer
            ? "session key to client-to-server sealing key magic constant\0"
            : "session key to server-to-client sealing key magic constant\0";

        byte[] sealKey = MD5.HashData(
            Concat(exportedSessionKey, Encoding.ASCII.GetBytes(sealMagic))
        );
        Rc4State rc4 = new Rc4State(sealKey);

        byte[] ciphertext = sealedMessage.AsSpan(16).ToArray();
        return rc4.Apply(ciphertext);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}

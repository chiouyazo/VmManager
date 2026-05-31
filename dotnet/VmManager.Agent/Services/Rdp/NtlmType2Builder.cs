using System.Text;

namespace VmManager.Agent.Services.Rdp;

public static class NtlmType2Builder
{
    private const string TargetName = "VMMANAGER";
    private const string DnsDomainName = "vmmanager.local";
    private const uint NegotiateFlags = 0xE2898215;

    public static byte[] Build(byte[] serverChallenge)
    {
        byte[] targetNameBytes = Encoding.Unicode.GetBytes(TargetName);
        byte[] targetInfo = BuildTargetInfo();

        int headerSize = 56;
        int targetNameOffset = headerSize;
        int targetInfoOffset = headerSize + targetNameBytes.Length;

        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("NTLMSSP\0"));
        writer.Write(2);

        writer.Write((short)targetNameBytes.Length);
        writer.Write((short)targetNameBytes.Length);
        writer.Write(targetNameOffset);

        writer.Write(unchecked((int)NegotiateFlags));
        writer.Write(serverChallenge);
        writer.Write(0L);

        writer.Write((short)targetInfo.Length);
        writer.Write((short)targetInfo.Length);
        writer.Write(targetInfoOffset);

        writer.Write((byte)10);
        writer.Write((byte)0);
        writer.Write((short)19041);
        writer.Write((short)0);
        writer.Write((byte)0);
        writer.Write((byte)15);

        writer.Write(targetNameBytes);
        writer.Write(targetInfo);

        return stream.ToArray();
    }

    private static byte[] BuildTargetInfo()
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);

        byte[] domain = Encoding.Unicode.GetBytes(TargetName);
        writer.Write((short)2);
        writer.Write((short)domain.Length);
        writer.Write(domain);

        byte[] computer = Encoding.Unicode.GetBytes(TargetName);
        writer.Write((short)1);
        writer.Write((short)computer.Length);
        writer.Write(computer);

        byte[] dnsDomain = Encoding.Unicode.GetBytes(DnsDomainName);
        writer.Write((short)4);
        writer.Write((short)dnsDomain.Length);
        writer.Write(dnsDomain);

        byte[] dnsComputer = Encoding.Unicode.GetBytes(DnsDomainName);
        writer.Write((short)3);
        writer.Write((short)dnsComputer.Length);
        writer.Write(dnsComputer);

        byte[] timestamp = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
        writer.Write((short)7);
        writer.Write((short)8);
        writer.Write(timestamp);

        writer.Write((short)0);
        writer.Write((short)0);

        return stream.ToArray();
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace VmManager.Agent.Services.Rdp;

public static class X224Handler
{
    public static async Task<byte[]> ReadPayloadAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        byte[] tpktHeader = new byte[4];
        await ReadExactAsync(stream, tpktHeader, cancellationToken);

        int totalLength = (tpktHeader[2] << 8) | tpktHeader[3];
        byte[] payload = new byte[totalLength - 4];
        await ReadExactAsync(stream, payload, cancellationToken);

        return payload;
    }

    public static byte[] BuildConnectionRequest(string cookie = "proxy")
    {
        byte[] cookieBytes = Encoding.ASCII.GetBytes("Cookie: mstshash=" + cookie + "\r\n");
        byte[] negotiation = { 0x01, 0x00, 0x08, 0x00, 0x0B, 0x00, 0x00, 0x00 };

        int x224Length = 7 + cookieBytes.Length + negotiation.Length;
        byte[] x224 = new byte[x224Length];
        x224[0] = (byte)(x224Length - 1);
        x224[1] = 0xE0;
        cookieBytes.CopyTo(x224, 7);
        negotiation.CopyTo(x224, 7 + cookieBytes.Length);

        int totalLength = 4 + x224Length;
        byte[] tpkt = new byte[totalLength];
        tpkt[0] = 0x03;
        tpkt[2] = (byte)(totalLength >> 8);
        tpkt[3] = (byte)totalLength;
        x224.CopyTo(tpkt, 4);

        return tpkt;
    }

    public static byte[] BuildConnectionConfirm(int selectedProtocol, byte flags)
    {
        byte[] x224 =
        {
            0x0E,
            0xD0,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x02,
            flags,
            0x08,
            0x00,
            (byte)selectedProtocol,
            (byte)(selectedProtocol >> 8),
            (byte)(selectedProtocol >> 16),
            (byte)(selectedProtocol >> 24),
        };

        int totalLength = 4 + x224.Length;
        byte[] tpkt = new byte[totalLength];
        tpkt[0] = 0x03;
        tpkt[2] = (byte)(totalLength >> 8);
        tpkt[3] = (byte)totalLength;
        x224.CopyTo(tpkt, 4);

        return tpkt;
    }

    public static (int SelectedProtocol, byte Flags) ParseConfirmResponse(byte[] payload)
    {
        int selectedProtocol = 0x08;
        byte flags = 0x3F;

        if (payload.Length >= 15)
        {
            flags = payload[8];
            selectedProtocol =
                payload[11] | (payload[12] << 8) | (payload[13] << 16) | (payload[14] << 24);
        }

        return (selectedProtocol, flags);
    }

    public static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new IOException("Connection closed during RDP handshake");
            offset += read;
        }
    }

    public static async Task<byte[]> ReadAvailableAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[16384];
        int read = await stream.ReadAsync(buffer, cancellationToken);
        if (read == 0)
            throw new IOException("Connection closed");
        return buffer.AsSpan(0, read).ToArray();
    }
}

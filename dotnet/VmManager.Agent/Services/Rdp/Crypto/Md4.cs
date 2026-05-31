using System.Text;

namespace VmManager.Agent.Services.Rdp.Crypto;

public static class Md4
{
    public static byte[] Hash(byte[] input)
    {
        uint[] state = { 0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476 };

        int padLen = 64 - ((input.Length + 9) % 64);
        if (padLen == 64)
            padLen = 0;

        byte[] padded = new byte[input.Length + 1 + padLen + 8];
        input.CopyTo(padded, 0);
        padded[input.Length] = 0x80;
        BitConverter.GetBytes((long)input.Length * 8).CopyTo(padded, padded.Length - 8);

        for (int block = 0; block < padded.Length; block += 64)
        {
            uint[] x = new uint[16];
            for (int i = 0; i < 16; i++)
                x[i] = BitConverter.ToUInt32(padded, block + i * 4);

            uint a = state[0];
            uint b = state[1];
            uint c = state[2];
            uint d = state[3];

            int[] shift1 = { 3, 7, 11, 19 };
            for (int i = 0; i < 16; i++)
            {
                uint f = (b & c) | (~b & d);
                a = RotateLeft(a + f + x[i], shift1[i % 4]);
                (a, b, c, d) = (d, a, b, c);
            }

            int[] round2Order = { 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15 };
            int[] shift2 = { 3, 5, 9, 13 };
            for (int i = 0; i < 16; i++)
            {
                uint f = (b & c) | (b & d) | (c & d);
                a = RotateLeft(a + f + x[round2Order[i]] + 0x5A827999, shift2[i % 4]);
                (a, b, c, d) = (d, a, b, c);
            }

            int[] round3Order = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 };
            int[] shift3 = { 3, 9, 11, 15 };
            for (int i = 0; i < 16; i++)
            {
                uint f = b ^ c ^ d;
                a = RotateLeft(a + f + x[round3Order[i]] + 0x6ED9EBA1, shift3[i % 4]);
                (a, b, c, d) = (d, a, b, c);
            }

            state[0] += a;
            state[1] += b;
            state[2] += c;
            state[3] += d;
        }

        byte[] result = new byte[16];
        for (int i = 0; i < 4; i++)
            BitConverter.GetBytes(state[i]).CopyTo(result, i * 4);

        return result;
    }

    public static byte[] ComputeNtHash(string password)
    {
        return Hash(Encoding.Unicode.GetBytes(password));
    }

    private static uint RotateLeft(uint value, int bits)
    {
        return (value << bits) | (value >> (32 - bits));
    }
}

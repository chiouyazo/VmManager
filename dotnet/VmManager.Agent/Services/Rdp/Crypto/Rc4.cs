namespace VmManager.Agent.Services.Rdp.Crypto;

public static class Rc4
{
    public static byte[] Transform(byte[] key, byte[] data)
    {
        byte[] state = new byte[256];
        for (int i = 0; i < 256; i++)
            state[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        byte[] output = new byte[data.Length];
        int x = 0;
        int y = 0;
        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[i] = (byte)(data[i] ^ state[(state[x] + state[y]) & 0xFF]);
        }

        return output;
    }
}

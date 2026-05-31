namespace VmManager.Agent.Services.Rdp.Crypto;

public sealed class Rc4State
{
    private readonly byte[] _state = new byte[256];
    private int _x;
    private int _y;

    public Rc4State(byte[] key)
    {
        for (int i = 0; i < 256; i++)
            _state[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + _state[i] + key[i % key.Length]) & 0xFF;
            (_state[i], _state[j]) = (_state[j], _state[i]);
        }
    }

    public byte[] Apply(byte[] data)
    {
        byte[] output = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            _x = (_x + 1) & 0xFF;
            _y = (_y + _state[_x]) & 0xFF;
            (_state[_x], _state[_y]) = (_state[_y], _state[_x]);
            output[i] = (byte)(data[i] ^ _state[(_state[_x] + _state[_y]) & 0xFF]);
        }

        return output;
    }
}

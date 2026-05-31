namespace VmManager.Agent.Services.Rdp.Crypto;

public static class Asn1Helper
{
    public static byte[] Wrap(byte tag, byte[] content)
    {
        using MemoryStream stream = new MemoryStream();
        stream.WriteByte(tag);

        if (content.Length < 128)
        {
            stream.WriteByte((byte)content.Length);
        }
        else if (content.Length < 256)
        {
            stream.WriteByte(0x81);
            stream.WriteByte((byte)content.Length);
        }
        else
        {
            stream.WriteByte(0x82);
            stream.WriteByte((byte)(content.Length >> 8));
            stream.WriteByte((byte)content.Length);
        }

        stream.Write(content);
        return stream.ToArray();
    }

    public static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    public static (int ContentStart, int ContentLength) ParseTagLength(byte[] data, int position)
    {
        position++;
        int length;

        if (data[position] == 0x82)
        {
            length = (data[position + 1] << 8) | data[position + 2];
            position += 3;
        }
        else if (data[position] == 0x81)
        {
            length = data[position + 1];
            position += 2;
        }
        else
        {
            length = data[position];
            position += 1;
        }

        return (position, length);
    }

    public static byte[] ExtractSubjectPublicKey(byte[] spki)
    {
        int pos = 0;

        // Skip outer SEQUENCE tag + length
        pos++;
        if (spki[pos] == 0x82)
            pos += 3;
        else if (spki[pos] == 0x81)
            pos += 2;
        else
            pos += 1;

        // Skip inner SEQUENCE (algorithm identifier)
        if (spki[pos] == 0x30)
        {
            pos++;
            int innerLen = spki[pos];
            if (innerLen == 0x82)
            {
                innerLen = (spki[pos + 1] << 8) | spki[pos + 2];
                pos += 3;
            }
            else if (innerLen == 0x81)
            {
                innerLen = spki[pos + 1];
                pos += 2;
            }
            else
                pos++;
            pos += innerLen;
        }

        // Now at BIT STRING containing the public key
        if (spki[pos] == 0x03)
        {
            pos++;
            int bitStrLen;
            if (spki[pos] == 0x82)
            {
                bitStrLen = (spki[pos + 1] << 8) | spki[pos + 2];
                pos += 3;
            }
            else if (spki[pos] == 0x81)
            {
                bitStrLen = spki[pos + 1];
                pos += 2;
            }
            else
            {
                bitStrLen = spki[pos];
                pos++;
            }

            pos++; // skip unused bits byte (0x00)
            bitStrLen--;

            byte[] result = new byte[bitStrLen];
            Array.Copy(spki, pos, result, 0, bitStrLen);
            return result;
        }

        return spki;
    }
}

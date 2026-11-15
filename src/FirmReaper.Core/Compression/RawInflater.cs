namespace FirmReaper.Core.Compression;

/// <summary>Resultado de descomprimir un flujo deflate crudo.</summary>
/// <param name="Data">Datos descomprimidos.</param>
/// <param name="BytesConsumed">Bytes de entrada usados hasta el final del último bloque (redondeado al byte).</param>
public sealed record InflateResult(byte[] Data, int BytesConsumed);

/// <summary>
/// Descompresor deflate crudo (RFC 1951) totalmente gestionado.
/// Se usa en lugar de <see cref="System.IO.Compression.DeflateStream"/> porque necesitamos saber
/// EXACTAMENTE en qué byte termina el flujo (equivale a <c>unused_data</c> de Python) para validar
/// la cola de 6 bytes y el "fin en flash" de la cabecera.
/// Basado en el algoritmo de referencia "puff" de Mark Adler (zlib/contrib/puff), reescrito.
/// Seguridad: limita el tamaño de salida (evita "bombas" de descompresión) y valida todos los códigos.
/// </summary>
public static class RawInflater
{
    private const int MaxBits = 15;
    private const int MaxLCodes = 286;
    private const int MaxDCodes = 30;
    private const int FixLCodes = 288;

    private static readonly ushort[] LenBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly byte[] LenExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly ushort[] DistBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly byte[] DistExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly byte[] CodeLenOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private sealed class Huffman(int symbols)
    {
        public readonly short[] Count = new short[MaxBits + 1];
        public readonly short[] Symbol = new short[symbols];
    }

    private ref struct State
    {
        public ReadOnlySpan<byte> In;
        public int InPos;
        public int BitBuf;
        public int BitCnt;
        public byte[] Out;
        public int OutPos;
        public int MaxOut;
    }

    /// <summary>Descomprime <paramref name="input"/> (deflate crudo, sin cabecera zlib).</summary>
    /// <param name="maxOutput">Tamaño máximo permitido de salida en bytes.</param>
    /// <exception cref="InvalidDataException">Flujo inválido, truncado o que excede <paramref name="maxOutput"/>.</exception>
    public static InflateResult Inflate(ReadOnlySpan<byte> input, int maxOutput)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutput);
        var s = new State { In = input, Out = new byte[Math.Min(maxOutput, Math.Max(1024, input.Length * 4))], MaxOut = maxOutput };
        int last;
        do
        {
            last = Bits(ref s, 1);
            int type = Bits(ref s, 2);
            switch (type)
            {
                case 0: Stored(ref s); break;
                case 1: Codes(ref s, FixedLen, FixedDist); break;
                case 2: Dynamic(ref s); break;
                default: throw new InvalidDataException("deflate: tipo de bloque inválido (3)");
            }
        } while (last == 0);

        var data = new byte[s.OutPos];
        Array.Copy(s.Out, data, s.OutPos);
        // Los bits sobrantes del último byte se descartan: el flujo termina en InPos.
        return new InflateResult(data, s.InPos);
    }

    private static int Bits(ref State s, int need)
    {
        int val = s.BitBuf;
        while (s.BitCnt < need)
        {
            if (s.InPos >= s.In.Length)
                throw new InvalidDataException("deflate: flujo truncado");
            val |= s.In[s.InPos++] << s.BitCnt;
            s.BitCnt += 8;
        }
        s.BitBuf = val >> need;
        s.BitCnt -= need;
        return val & ((1 << need) - 1);
    }

    private static void Put(ref State s, byte b)
    {
        if (s.OutPos >= s.Out.Length)
        {
            if (s.OutPos >= s.MaxOut)
                throw new InvalidDataException($"deflate: la salida supera el máximo permitido ({s.MaxOut} bytes)");
            int newSize = (int)Math.Min((long)s.MaxOut, (long)s.Out.Length * 2);
            Array.Resize(ref s.Out, newSize);
        }
        s.Out[s.OutPos++] = b;
    }

    private static void Stored(ref State s)
    {
        s.BitBuf = 0;
        s.BitCnt = 0;
        if (s.InPos + 4 > s.In.Length)
            throw new InvalidDataException("deflate: bloque almacenado truncado");
        int len = s.In[s.InPos] | (s.In[s.InPos + 1] << 8);
        int nlen = s.In[s.InPos + 2] | (s.In[s.InPos + 3] << 8);
        s.InPos += 4;
        if (len != (~nlen & 0xFFFF))
            throw new InvalidDataException("deflate: longitud de bloque almacenado incoherente");
        if (s.InPos + len > s.In.Length)
            throw new InvalidDataException("deflate: bloque almacenado truncado");
        for (int i = 0; i < len; i++)
            Put(ref s, s.In[s.InPos++]);
    }

    private static int Decode(ref State s, Huffman h)
    {
        int code = 0, first = 0, index = 0;
        for (int len = 1; len <= MaxBits; len++)
        {
            code |= Bits(ref s, 1);
            int count = h.Count[len];
            if (code - count < first)
                return h.Symbol[index + (code - first)];
            index += count;
            first += count;
            first <<= 1;
            code <<= 1;
        }
        throw new InvalidDataException("deflate: código Huffman inválido");
    }

    /// <returns>0 si el código es completo, &gt;0 si es incompleto, &lt;0 si está sobresuscrito.</returns>
    private static int Construct(Huffman h, ReadOnlySpan<short> length)
    {
        Array.Clear(h.Count);
        foreach (short l in length) h.Count[l]++;
        if (h.Count[0] == length.Length) return 0;

        int left = 1;
        for (int len = 1; len <= MaxBits; len++)
        {
            left <<= 1;
            left -= h.Count[len];
            if (left < 0) return left;
        }

        Span<short> offs = stackalloc short[MaxBits + 1];
        offs[1] = 0;
        for (int len = 1; len < MaxBits; len++)
            offs[len + 1] = (short)(offs[len] + h.Count[len]);
        for (short symbol = 0; symbol < length.Length; symbol++)
            if (length[symbol] != 0)
                h.Symbol[offs[length[symbol]]++] = symbol;
        return left;
    }

    private static void Codes(ref State s, Huffman lencode, Huffman distcode)
    {
        while (true)
        {
            int symbol = Decode(ref s, lencode);
            if (symbol < 256)
            {
                Put(ref s, (byte)symbol);
            }
            else if (symbol == 256)
            {
                return;
            }
            else
            {
                symbol -= 257;
                if (symbol >= 29)
                    throw new InvalidDataException("deflate: símbolo de longitud inválido");
                int len = LenBase[symbol] + Bits(ref s, LenExtra[symbol]);
                int dsym = Decode(ref s, distcode);
                if (dsym >= 30)
                    throw new InvalidDataException("deflate: símbolo de distancia inválido");
                int dist = DistBase[dsym] + Bits(ref s, DistExtra[dsym]);
                if (dist > s.OutPos)
                    throw new InvalidDataException("deflate: distancia hacia atrás fuera de los datos");
                for (int i = 0; i < len; i++)
                    Put(ref s, s.Out[s.OutPos - dist]);
            }
        }
    }

    private static readonly Huffman FixedLen = BuildFixedLen();
    private static readonly Huffman FixedDist = BuildFixedDist();

    private static Huffman BuildFixedLen()
    {
        var lengths = new short[FixLCodes];
        int i = 0;
        for (; i < 144; i++) lengths[i] = 8;
        for (; i < 256; i++) lengths[i] = 9;
        for (; i < 280; i++) lengths[i] = 7;
        for (; i < FixLCodes; i++) lengths[i] = 8;
        var h = new Huffman(FixLCodes);
        Construct(h, lengths);
        return h;
    }

    private static Huffman BuildFixedDist()
    {
        var lengths = new short[MaxDCodes];
        Array.Fill(lengths, (short)5);
        var h = new Huffman(MaxDCodes);
        Construct(h, lengths);
        return h;
    }

    private static void Dynamic(ref State s)
    {
        int nlen = Bits(ref s, 5) + 257;
        int ndist = Bits(ref s, 5) + 1;
        int ncode = Bits(ref s, 4) + 4;
        if (nlen > MaxLCodes || ndist > MaxDCodes)
            throw new InvalidDataException("deflate: demasiados códigos en bloque dinámico");

        var lengths = new short[MaxLCodes + MaxDCodes];
        int index;
        for (index = 0; index < ncode; index++)
            lengths[CodeLenOrder[index]] = (short)Bits(ref s, 3);
        for (; index < 19; index++)
            lengths[CodeLenOrder[index]] = 0;

        var lencode = new Huffman(MaxLCodes);
        var distcode = new Huffman(MaxDCodes);
        if (Construct(lencode, lengths.AsSpan(0, 19)) != 0)
            throw new InvalidDataException("deflate: código de longitudes incompleto");

        index = 0;
        while (index < nlen + ndist)
        {
            int symbol = Decode(ref s, lencode);
            if (symbol < 16)
            {
                lengths[index++] = (short)symbol;
            }
            else
            {
                short len = 0;
                if (symbol == 16)
                {
                    if (index == 0)
                        throw new InvalidDataException("deflate: repetición sin longitud previa");
                    len = lengths[index - 1];
                    symbol = 3 + Bits(ref s, 2);
                }
                else if (symbol == 17)
                {
                    symbol = 3 + Bits(ref s, 3);
                }
                else
                {
                    symbol = 11 + Bits(ref s, 7);
                }
                if (index + symbol > nlen + ndist)
                    throw new InvalidDataException("deflate: demasiadas longitudes");
                while (symbol-- > 0)
                    lengths[index++] = len;
            }
        }

        if (lengths[256] == 0)
            throw new InvalidDataException("deflate: falta el código de fin de bloque");

        int err = Construct(lencode, lengths.AsSpan(0, nlen));
        if (err < 0 || (err > 0 && nlen - lencode.Count[0] != 1))
            throw new InvalidDataException("deflate: código de literales/longitudes inválido");

        err = Construct(distcode, lengths.AsSpan(nlen, ndist));
        if (err < 0 || (err > 0 && ndist - distcode.Count[0] != 1))
            throw new InvalidDataException("deflate: código de distancias inválido");

        Codes(ref s, lencode, distcode);
    }
}

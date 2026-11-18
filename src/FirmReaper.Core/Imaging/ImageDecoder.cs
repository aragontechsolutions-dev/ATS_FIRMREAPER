using System.Buffers.Binary;
using FirmReaper.Core.Compression;

namespace FirmReaper.Core.Imaging;

/// <summary>Imagen RGB en memoria (3 bytes por píxel, filas de arriba abajo).</summary>
public sealed record RgbImage(byte[] Pixels, int Width, int Height);

/// <summary>
/// Lee la imagen que da el usuario para el logo: JPEG, PNG o BMP. Sin bibliotecas nativas (el mismo
/// resultado en Windows y en Linux). El archivo es un dato NO confiable: se limitan las dimensiones y el
/// tamaño de lo descomprimido, se comprueban los CRC del PNG y cualquier incoherencia lanza
/// <see cref="InvalidDataException"/>. La transparencia se mezcla sobre fondo negro.
/// </summary>
public static class ImageDecoder
{
    /// <summary>Máximo de píxeles aceptado (p. ej. 6000x6000).</summary>
    public const long MaxPixels = 36_000_000;

    public static RgbImage Decode(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length >= 3 && file[0] == 0xFF && file[1] == 0xD8 && file[2] == 0xFF)
        {
            var rgb = LogoImage.DecodeRgb(file, out int w, out int h);
            return new RgbImage(rgb, w, h);
        }
        if (file.AsSpan().StartsWith(PngSignature)) return DecodePng(file);
        if (file.Length >= 2 && file[0] == (byte)'B' && file[1] == (byte)'M') return DecodeBmp(file);
        throw new InvalidDataException("Formato de imagen no reconocido (se admite JPEG, PNG o BMP).");
    }

    private static void CheckSize(long w, long h)
    {
        if (w < 1 || h < 1 || w > 16384 || h > 16384 || w * h > MaxPixels)
            throw new InvalidDataException($"Dimensiones de imagen no admitidas ({w}x{h}).");
    }

    // ------------------------------------------------------------------------------------------- PNG
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] Crc32Table = BuildCrc32();

    private static uint[] BuildCrc32()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(ReadOnlySpan<byte> d)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in d) c = Crc32Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static RgbImage DecodePng(byte[] f)
    {
        static InvalidDataException Bad(string m) => new("PNG no válido: " + m);
        int p = 8;
        int w = 0, h = 0, depth = 0, ctype = -1;
        byte[]? palette = null;
        byte[]? trns = null;
        using var idat = new MemoryStream();
        bool end = false;
        while (!end)
        {
            if (p + 12 > f.Length) throw Bad("termina antes de IEND.");
            uint len = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(p));
            if (len > int.MaxValue || p + 12 + (long)len > f.Length) throw Bad("bloque fuera de límites.");
            var type = f.AsSpan(p + 4, 4);
            var data = f.AsSpan(p + 8, (int)len);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(p + 8 + (int)len));
            if (Crc32(f.AsSpan(p + 4, 4 + (int)len)) != crc) throw Bad("CRC incorrecto (archivo dañado).");
            string t = System.Text.Encoding.ASCII.GetString(type);
            switch (t)
            {
                case "IHDR":
                    if (len != 13 || ctype >= 0) throw Bad("IHDR incorrecto.");
                    w = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), int.MaxValue);
                    h = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[4..]), int.MaxValue);
                    CheckSize(w, h);
                    depth = data[8];
                    ctype = data[9];
                    if (data[10] != 0 || data[11] != 0) throw Bad("compresión o filtro desconocidos.");
                    if (data[12] != 0) throw new InvalidDataException("PNG entrelazado (Adam7) no admitido: guárdelo sin entrelazar.");
                    bool okDepth = ctype switch
                    {
                        0 => depth is 1 or 2 or 4 or 8 or 16,
                        2 or 4 or 6 => depth is 8 or 16,
                        3 => depth is 1 or 2 or 4 or 8,
                        _ => false,
                    };
                    if (!okDepth) throw Bad($"combinación tipo de color {ctype} / {depth} bits no válida.");
                    break;
                case "PLTE":
                    if (len % 3 != 0 || len == 0 || len > 768) throw Bad("PLTE incorrecto.");
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    trns = data.ToArray();
                    break;
                case "IDAT":
                    if (ctype < 0) throw Bad("IDAT antes de IHDR.");
                    idat.Write(data);
                    if (idat.Length > 512L * 1024 * 1024) throw Bad("demasiados datos.");
                    break;
                case "IEND":
                    end = true;
                    break;
                default:
                    // Bloque crítico desconocido (primera letra mayúscula) → no se puede interpretar.
                    if ((type[0] & 0x20) == 0) throw Bad($"bloque crítico desconocido '{t}'.");
                    break;
            }
            p += 12 + (int)len;
        }
        if (ctype < 0) throw Bad("falta IHDR.");
        if (ctype == 3 && palette is null) throw Bad("falta la paleta (PLTE).");

        int channels = ctype switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int bitsPerPixel = channels * depth;
        int stride = (int)(((long)w * bitsPerPixel + 7) / 8);
        long rawLen = (long)h * (stride + 1);
        if (rawLen > int.MaxValue) throw Bad("imagen demasiado grande.");

        // zlib: cabecera de 2 bytes + deflate + Adler-32
        var z = idat.ToArray();
        if (z.Length < 6) throw Bad("datos comprimidos vacíos.");
        int cmf = z[0], flg = z[1];
        if ((cmf & 15) != 8 || (cmf >> 4) > 7 || ((cmf << 8) | flg) % 31 != 0 || (flg & 0x20) != 0)
            throw Bad("cabecera zlib incorrecta.");
        var inf = RawInflater.Inflate(z.AsSpan(2), (int)rawLen);
        var raw = inf.Data;
        if (raw.Length != rawLen) throw Bad("los datos no tienen el tamaño de la imagen.");
        if (2 + inf.BytesConsumed + 4 <= z.Length &&
            BinaryPrimitives.ReadUInt32BigEndian(z.AsSpan(2 + inf.BytesConsumed)) != Adler32(raw))
            throw Bad("Adler-32 incorrecto (archivo dañado).");

        // Filtros por fila
        int bpp = Math.Max(1, bitsPerPixel / 8);
        var cur = new byte[stride];
        var prev = new byte[stride];
        var outPix = new byte[(long)w * h * 3];
        for (int y = 0; y < h; y++)
        {
            int ro = y * (stride + 1);
            int filter = raw[ro];
            Array.Copy(raw, ro + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = filter switch
                {
                    0 => cur[i],
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + ((a + b) >> 1)),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => throw Bad($"filtro de fila desconocido ({filter})."),
                };
            }
            for (int x = 0; x < w; x++)
            {
                int r, g, bl, al = 255;
                switch (ctype)
                {
                    case 0:
                    {
                        int v = Sample(cur, x, depth, out int full);
                        r = g = bl = v;
                        if (trns is { Length: >= 2 } && full == BinaryPrimitives.ReadUInt16BigEndian(trns)) al = 0;
                        break;
                    }
                    case 3:
                    {
                        Sample(cur, x, depth, out int idx);
                        if (idx * 3 + 2 >= palette!.Length) throw Bad("índice de paleta fuera de rango.");
                        r = palette[idx * 3]; g = palette[idx * 3 + 1]; bl = palette[idx * 3 + 2];
                        if (trns is not null && idx < trns.Length) al = trns[idx];
                        break;
                    }
                    default:
                    {
                        int bytesPerSample = depth / 8;
                        int o = x * channels * bytesPerSample;
                        int S(int ch) => cur[o + ch * bytesPerSample]; // byte alto en 16 bits
                        int Full(int ch) => bytesPerSample == 2 ? (cur[o + ch * 2] << 8) | cur[o + ch * 2 + 1] : cur[o + ch];
                        if (ctype == 2)
                        {
                            r = S(0); g = S(1); bl = S(2);
                            if (trns is { Length: >= 6 } && Full(0) == BinaryPrimitives.ReadUInt16BigEndian(trns) &&
                                Full(1) == BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(2)) &&
                                Full(2) == BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(4))) al = 0;
                        }
                        else if (ctype == 4)
                        {
                            r = g = bl = S(0);
                            al = S(1);
                        }
                        else
                        {
                            r = S(0); g = S(1); bl = S(2);
                            al = S(3);
                        }
                        break;
                    }
                }
                long q = ((long)y * w + x) * 3;
                outPix[q] = Over(r, al);
                outPix[q + 1] = Over(g, al);
                outPix[q + 2] = Over(bl, al);
            }
            (prev, cur) = (cur, prev);
        }
        return new RgbImage(outPix, w, h);
    }

    /// <summary>Muestra de 1/2/4/8/16 bits escalada a 0..255 (y su valor completo para tRNS).</summary>
    private static int Sample(byte[] row, int x, int depth, out int full)
    {
        switch (depth)
        {
            case 8:
                full = row[x];
                return full;
            case 16:
                full = (row[x * 2] << 8) | row[x * 2 + 1];
                return row[x * 2];
            default:
                int perByte = 8 / depth;
                int shift = 8 - depth * (x % perByte + 1);
                full = (row[x / perByte] >> shift) & ((1 << depth) - 1);
                return full * 255 / ((1 << depth) - 1);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte Over(int c, int a) => (byte)((c * a + 127) / 255);

    private static uint Adler32(byte[] d)
    {
        uint s1 = 1, s2 = 0;
        foreach (byte b in d)
        {
            s1 = (s1 + b) % 65521;
            s2 = (s2 + s1) % 65521;
        }
        return (s2 << 16) | s1;
    }

    // ------------------------------------------------------------------------------------------- BMP
    private static RgbImage DecodeBmp(byte[] f)
    {
        static InvalidDataException Bad(string m) => new("BMP no válido: " + m);
        if (f.Length < 54) throw Bad("demasiado corto.");
        uint dataOff = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(10));
        uint dib = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(14));
        if (dib < 40 || 14 + dib > f.Length) throw Bad("cabecera DIB no admitida.");
        int w = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(18));
        int hRaw = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(22));
        if (hRaw == int.MinValue) throw Bad("alto no válido.");
        bool topDown = hRaw < 0;
        int h = Math.Abs(hRaw);
        CheckSize(w, h);
        int planes = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(26));
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(28));
        uint comp = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(30));
        if (planes != 1) throw Bad("planos ≠ 1.");
        if (bpp is not (24 or 32)) throw new InvalidDataException($"BMP de {bpp} bits no admitido: guárdelo en 24 bits (color verdadero).");
        // BI_RGB, o BI_BITFIELDS con las máscaras estándar BGR(A).
        if (comp == 3)
        {
            if (dib < 52 && f.Length < 14 + 40 + 12) throw Bad("faltan las máscaras.");
            int mo = 14 + 40;
            uint rm = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(mo));
            uint gm = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(mo + 4));
            uint bm = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(mo + 8));
            if (bpp != 32 || rm != 0x00FF0000 || gm != 0x0000FF00 || bm != 0x000000FF)
                throw new InvalidDataException("BMP con máscaras de color no estándar: guárdelo en 24 bits.");
        }
        else if (comp != 0)
        {
            throw new InvalidDataException("BMP comprimido no admitido: guárdelo sin compresión (24 bits).");
        }
        int bytesPP = bpp / 8;
        long stride = ((long)w * bytesPP + 3) & ~3L;
        if (dataOff + stride * h > f.Length) throw Bad("los datos de la imagen están incompletos.");
        var px = new byte[(long)w * h * 3];
        for (int y = 0; y < h; y++)
        {
            int sy = topDown ? y : h - 1 - y;
            long ro = dataOff + sy * stride;
            for (int x = 0; x < w; x++)
            {
                long s = ro + (long)x * bytesPP;
                long d = ((long)y * w + x) * 3;
                px[d] = f[s + 2];
                px[d + 1] = f[s + 1];
                px[d + 2] = f[s];
            }
        }
        return new RgbImage(px, w, h);
    }
}

namespace FirmReaper.Core.Imaging;

/// <summary>
/// Codificador JPEG baseline (SOF0) que reproduce BYTE A BYTE la salida de libjpeg-turbo con la
/// configuración que usa Pillow en patch_mirror_menu.py:
///   RGB → YCbCr (jccolor.c), 4:4:4, DCT entera "islow" (jfdctint.c), cuantización por recíprocos
///   (jcdctmgr.c), tablas de Huffman estándar (Anexo K, sin optimizar), intervalo de reinicio en MCU,
///   cabecera JFIF por defecto (jcmarker.c).
/// Todo es aritmética entera: el resultado es idéntico en cualquier plataforma.
/// </summary>
public static class JpegBaselineEncoder
{
    private const int ScaleBits = 16;
    private const int CbCrOffset = 128 << ScaleBits;
    private const int OneHalf = 1 << (ScaleBits - 1);

    private const int ConstBits = 13;
    private const int Pass1Bits = 2;

    private static int Fix(double x) => (int)(x * (1 << ScaleBits) + 0.5);

    /// <summary>Orden zigzag → natural (jpeg_natural_order).</summary>
    public static readonly int[] NaturalOrder =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static readonly byte[] DcLumaBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DcLumaVals =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B,
    ];

    private static readonly byte[] AcLumaBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125];
    private static readonly byte[] AcLumaVals =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    private static readonly byte[] DcChromaBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    private static readonly byte[] DcChromaVals =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B,
    ];

    private static readonly byte[] AcChromaBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119];
    private static readonly byte[] AcChromaVals =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>
    /// Codifica una imagen RGB (3 bytes por píxel, filas de arriba abajo).
    /// </summary>
    /// <param name="quantLuma">Tabla de cuantización de Y en ORDEN NATURAL (64 valores 1..255).</param>
    /// <param name="quantChroma">Tabla de Cb y Cr en orden natural.</param>
    /// <param name="restartInterval">MCU entre marcadores RST (0 = sin reinicios).</param>
    public static byte[] Encode(byte[] rgb, int width, int height, int[] quantLuma, int[] quantChroma, int restartInterval)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (width < 1 || height < 1 || width > 65535 || height > 65535) throw new ArgumentOutOfRangeException(nameof(width));
        if (rgb.Length != width * height * 3) throw new ArgumentException("Tamaño de píxeles incorrecto.", nameof(rgb));
        if (quantLuma.Length != 64 || quantChroma.Length != 64) throw new ArgumentException("Las tablas deben tener 64 valores.");
        if (quantLuma.Concat(quantChroma).Any(q => q is < 1 or > 255)) throw new ArgumentException("Valores de cuantización fuera de 1..255 (baseline).");
        if (restartInterval is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(restartInterval));

        int wBlocks = (width + 7) / 8, hBlocks = (height + 7) / 8;
        int pw = wBlocks * 8, ph = hBlocks * 8;

        // 1) Color + bordes replicados (jccolor.c + expand_right_edge / filas finales de jcprepare.c)
        var planes = new byte[3][];
        for (int c = 0; c < 3; c++) planes[c] = new byte[pw * ph];
        var tab = BuildRgbYccTable();
        for (int y = 0; y < ph; y++)
        {
            int sy = Math.Min(y, height - 1);
            for (int x = 0; x < pw; x++)
            {
                int sx = Math.Min(x, width - 1);
                int p = (sy * width + sx) * 3;
                int r = rgb[p], g = rgb[p + 1], b = rgb[p + 2];
                int o = y * pw + x;
                planes[0][o] = (byte)((tab[r + RY] + tab[g + GY] + tab[b + BY]) >> ScaleBits);
                planes[1][o] = (byte)((tab[r + RCb] + tab[g + GCb] + tab[b + BCb]) >> ScaleBits);
                planes[2][o] = (byte)((tab[r + RCr] + tab[g + GCr] + tab[b + BCr]) >> ScaleBits);
            }
        }

        // 2) Divisores de cuantización (compute_reciprocal de jcdctmgr.c, DCTELEM de 16 bits)
        var divL = Divisors(quantLuma);
        var divC = Divisors(quantChroma);

        // 3) Codificación entrópica
        var w = new JpegWriter();
        w.WriteHeaders(width, height, quantLuma, quantChroma, restartInterval);
        var huffDcL = new HuffTable(DcLumaBits, DcLumaVals);
        var huffAcL = new HuffTable(AcLumaBits, AcLumaVals);
        var huffDcC = new HuffTable(DcChromaBits, DcChromaVals);
        var huffAcC = new HuffTable(AcChromaBits, AcChromaVals);

        var block = new int[64];
        var coef = new int[64];
        var lastDc = new int[3];
        int restartsToGo = restartInterval;
        int nextRestart = 0;
        for (int by = 0; by < hBlocks; by++)
        {
            for (int bx = 0; bx < wBlocks; bx++)
            {
                if (restartInterval != 0)
                {
                    if (restartsToGo == 0)
                    {
                        w.EmitRestart(nextRestart);
                        nextRestart = (nextRestart + 1) & 7;
                        restartsToGo = restartInterval;
                        Array.Clear(lastDc);
                    }
                    restartsToGo--;
                }
                for (int c = 0; c < 3; c++)
                {
                    var plane = planes[c];
                    for (int yy = 0; yy < 8; yy++)
                        for (int xx = 0; xx < 8; xx++)
                            block[yy * 8 + xx] = plane[(by * 8 + yy) * pw + bx * 8 + xx] - 128;
                    ForwardDctIslow(block);
                    Quantize(block, c == 0 ? divL : divC, coef);
                    if (c == 0) w.EncodeBlock(coef, ref lastDc[0], huffDcL, huffAcL);
                    else w.EncodeBlock(coef, ref lastDc[c], huffDcC, huffAcC);
                }
            }
        }
        w.Finish();
        return w.ToArray();
    }

    // ---------------------------------------------------------------- color (jccolor.c)
    private const int RY = 0, GY = 256, BY = 512, RCb = 768, GCb = 1024, BCb = 1280, RCr = BCb, GCr = 1536, BCr = 1792;

    private static int[] BuildRgbYccTable()
    {
        var t = new int[2048];
        for (int i = 0; i < 256; i++)
        {
            t[i + RY] = Fix(0.29900) * i;
            t[i + GY] = Fix(0.58700) * i;
            t[i + BY] = Fix(0.11400) * i + OneHalf;
            t[i + RCb] = -Fix(0.16874) * i;
            t[i + GCb] = -Fix(0.33126) * i;
            // B=>Cb y R=>Cr comparten tabla; OneHalf-1 para que el máximo no se desborde.
            t[i + BCb] = Fix(0.50000) * i + CbCrOffset + OneHalf - 1;
            t[i + GCr] = -Fix(0.41869) * i;
            t[i + BCr] = -Fix(0.08131) * i;
        }
        return t;
    }

    // ---------------------------------------------------------------- DCT entera (jfdctint.c)
    private const int Fix_0_298631336 = 2446, Fix_0_390180644 = 3196, Fix_0_541196100 = 4433, Fix_0_765366865 = 6270,
        Fix_0_899976223 = 7373, Fix_1_175875602 = 9633, Fix_1_501321110 = 12299, Fix_1_847759065 = 15137,
        Fix_1_961570560 = 16069, Fix_2_053119869 = 16819, Fix_2_562915447 = 20995, Fix_3_072711026 = 25172;

    private static int Descale(int x, int n) => (x + (1 << (n - 1))) >> n;

    private static void ForwardDctIslow(int[] d)
    {
        // Paso 1: filas. Resultado escalado por 2^Pass1Bits.
        for (int r = 0; r < 64; r += 8)
        {
            int tmp0 = d[r + 0] + d[r + 7], tmp7 = d[r + 0] - d[r + 7];
            int tmp1 = d[r + 1] + d[r + 6], tmp6 = d[r + 1] - d[r + 6];
            int tmp2 = d[r + 2] + d[r + 5], tmp5 = d[r + 2] - d[r + 5];
            int tmp3 = d[r + 3] + d[r + 4], tmp4 = d[r + 3] - d[r + 4];

            int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            d[r + 0] = (short)((tmp10 + tmp11) << Pass1Bits);
            d[r + 4] = (short)((tmp10 - tmp11) << Pass1Bits);

            int z1 = (tmp12 + tmp13) * Fix_0_541196100;
            d[r + 2] = (short)Descale(z1 + tmp13 * Fix_0_765366865, ConstBits - Pass1Bits);
            d[r + 6] = (short)Descale(z1 + tmp12 * -Fix_1_847759065, ConstBits - Pass1Bits);

            z1 = tmp4 + tmp7;
            int z2 = tmp5 + tmp6, z3 = tmp4 + tmp6, z4 = tmp5 + tmp7;
            int z5 = (z3 + z4) * Fix_1_175875602;
            tmp4 *= Fix_0_298631336; tmp5 *= Fix_2_053119869; tmp6 *= Fix_3_072711026; tmp7 *= Fix_1_501321110;
            z1 *= -Fix_0_899976223; z2 *= -Fix_2_562915447; z3 *= -Fix_1_961570560; z4 *= -Fix_0_390180644;
            z3 += z5; z4 += z5;
            d[r + 7] = (short)Descale(tmp4 + z1 + z3, ConstBits - Pass1Bits);
            d[r + 5] = (short)Descale(tmp5 + z2 + z4, ConstBits - Pass1Bits);
            d[r + 3] = (short)Descale(tmp6 + z2 + z3, ConstBits - Pass1Bits);
            d[r + 1] = (short)Descale(tmp7 + z1 + z4, ConstBits - Pass1Bits);
        }
        // Paso 2: columnas. Quita el factor Pass1Bits; la salida queda escalada por 8.
        for (int c = 0; c < 8; c++)
        {
            int tmp0 = d[c + 0] + d[c + 56], tmp7 = d[c + 0] - d[c + 56];
            int tmp1 = d[c + 8] + d[c + 48], tmp6 = d[c + 8] - d[c + 48];
            int tmp2 = d[c + 16] + d[c + 40], tmp5 = d[c + 16] - d[c + 40];
            int tmp3 = d[c + 24] + d[c + 32], tmp4 = d[c + 24] - d[c + 32];

            int tmp10 = tmp0 + tmp3, tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2, tmp12 = tmp1 - tmp2;

            d[c + 0] = (short)Descale(tmp10 + tmp11, Pass1Bits);
            d[c + 32] = (short)Descale(tmp10 - tmp11, Pass1Bits);

            int z1 = (tmp12 + tmp13) * Fix_0_541196100;
            d[c + 16] = (short)Descale(z1 + tmp13 * Fix_0_765366865, ConstBits + Pass1Bits);
            d[c + 48] = (short)Descale(z1 + tmp12 * -Fix_1_847759065, ConstBits + Pass1Bits);

            z1 = tmp4 + tmp7;
            int z2 = tmp5 + tmp6, z3 = tmp4 + tmp6, z4 = tmp5 + tmp7;
            int z5 = (z3 + z4) * Fix_1_175875602;
            tmp4 *= Fix_0_298631336; tmp5 *= Fix_2_053119869; tmp6 *= Fix_3_072711026; tmp7 *= Fix_1_501321110;
            z1 *= -Fix_0_899976223; z2 *= -Fix_2_562915447; z3 *= -Fix_1_961570560; z4 *= -Fix_0_390180644;
            z3 += z5; z4 += z5;
            d[c + 56] = (short)Descale(tmp4 + z1 + z3, ConstBits + Pass1Bits);
            d[c + 40] = (short)Descale(tmp5 + z2 + z4, ConstBits + Pass1Bits);
            d[c + 24] = (short)Descale(tmp6 + z2 + z3, ConstBits + Pass1Bits);
            d[c + 8] = (short)Descale(tmp7 + z1 + z4, ConstBits + Pass1Bits);
        }
    }

    // ---------------------------------------------------------------- cuantización (jcdctmgr.c)
    private readonly record struct Divisor(ushort Recip, ushort Corr, int Shift);

    private static Divisor[] Divisors(int[] qtable)
    {
        var d = new Divisor[64];
        for (int i = 0; i < 64; i++)
        {
            uint divisor = (uint)qtable[i] << 3; // la DCT islow sale escalada por 8
            // compute_reciprocal (DCTELEM de 16 bits, UDCTELEM2 de 32 bits)
            int b = 31 - System.Numerics.BitOperations.LeadingZeroCount(divisor); // flss(divisor) - 1
            int r = 16 + b;
            uint fq = (uint)((1UL << r) / divisor);
            uint fr = (uint)((1UL << r) % divisor);
            uint c = divisor / 2;
            if (fr == 0)
            {
                fq >>= 1;
                r--;
            }
            else if (fr <= divisor / 2U)
            {
                c++;
            }
            else
            {
                fq++;
            }
            d[i] = new Divisor((ushort)fq, (ushort)c, r - 16);
        }
        return d;
    }

    private static void Quantize(int[] workspace, Divisor[] div, int[] output)
    {
        for (int i = 0; i < 64; i++)
        {
            int temp = (short)workspace[i];
            var dv = div[i];
            if (temp < 0)
            {
                temp = -temp;
                uint product = (uint)(temp + dv.Corr) * dv.Recip;
                product >>= dv.Shift + 16;
                temp = -(short)product;
            }
            else
            {
                uint product = (uint)(temp + dv.Corr) * dv.Recip;
                product >>= dv.Shift + 16;
                temp = (short)product;
            }
            output[i] = temp;
        }
    }

    // ---------------------------------------------------------------- Huffman (jchuff.c)
    private sealed class HuffTable
    {
        public readonly byte[] Bits;
        public readonly byte[] Vals;
        public readonly uint[] Code = new uint[256];
        public readonly byte[] Size = new byte[256];

        public HuffTable(byte[] bits, byte[] vals)
        {
            Bits = bits;
            Vals = vals;
            // jpeg_make_c_derived_tbl: códigos canónicos
            var huffsize = new List<int>();
            for (int l = 1; l <= 16; l++)
                for (int i = 0; i < bits[l - 1]; i++) huffsize.Add(l);
            uint code = 0;
            int si = huffsize.Count > 0 ? huffsize[0] : 0;
            var huffcode = new uint[huffsize.Count];
            for (int p = 0; p < huffsize.Count;)
            {
                while (p < huffsize.Count && huffsize[p] == si) huffcode[p++] = code++;
                code <<= 1;
                si++;
            }
            for (int p = 0; p < vals.Length; p++)
            {
                Code[vals[p]] = huffcode[p];
                Size[vals[p]] = (byte)huffsize[p];
            }
        }
    }

    private sealed class JpegWriter
    {
        private readonly MemoryStream _ms = new();
        private ulong _buf;
        private int _bits;

        private void Byte(int b) => _ms.WriteByte((byte)b);

        private void Word(int v)
        {
            Byte(v >> 8);
            Byte(v & 0xFF);
        }

        public void WriteHeaders(int width, int height, int[] ql, int[] qc, int restart)
        {
            Byte(0xFF); Byte(0xD8);                                   // SOI
            Byte(0xFF); Byte(0xE0); Word(16);                         // APP0 JFIF por defecto
            foreach (var c in "JFIF\0"u8) Byte(c);
            Byte(1); Byte(1); Byte(0); Word(1); Word(1); Byte(0); Byte(0);
            Dqt(0, ql);
            Dqt(1, qc);
            Byte(0xFF); Byte(0xC0); Word(8 + 3 * 3);                  // SOF0
            Byte(8); Word(height); Word(width); Byte(3);
            Byte(1); Byte(0x11); Byte(0);
            Byte(2); Byte(0x11); Byte(1);
            Byte(3); Byte(0x11); Byte(1);
            Dht(0x00, DcLumaBits, DcLumaVals);                        // como write_scan_header
            Dht(0x10, AcLumaBits, AcLumaVals);
            Dht(0x01, DcChromaBits, DcChromaVals);
            Dht(0x11, AcChromaBits, AcChromaVals);
            if (restart != 0)
            {
                Byte(0xFF); Byte(0xDD); Word(4); Word(restart);       // DRI
            }
            Byte(0xFF); Byte(0xDA); Word(6 + 2 * 3); Byte(3);         // SOS
            Byte(1); Byte(0x00);
            Byte(2); Byte(0x11);
            Byte(3); Byte(0x11);
            Byte(0); Byte(63); Byte(0);
        }

        private void Dqt(int id, int[] q)
        {
            Byte(0xFF); Byte(0xDB); Word(2 + 65);
            Byte(id);
            for (int i = 0; i < 64; i++) Byte(q[NaturalOrder[i]]);
        }

        private void Dht(int tc, byte[] bits, byte[] vals)
        {
            Byte(0xFF); Byte(0xC4); Word(2 + 1 + 16 + vals.Length);
            Byte(tc);
            foreach (var b in bits) Byte(b);
            foreach (var v in vals) Byte(v);
        }

        private void Emit(uint code, int size)
        {
            _buf = (_buf << size) | (code & ((1u << size) - 1));
            _bits += size;
            while (_bits >= 8)
            {
                int c = (int)((_buf >> (_bits - 8)) & 0xFF);
                Byte(c);
                if (c == 0xFF) Byte(0); // relleno de bytes
                _bits -= 8;
            }
        }

        /// <summary>Rellena con unos hasta el byte (flush_bits).</summary>
        private void FlushBits()
        {
            Emit(0x7F, 7);
            _buf = 0;
            _bits = 0;
        }

        public void EmitRestart(int n)
        {
            FlushBits();
            Byte(0xFF);
            Byte(0xD0 + n);
        }

        public void EncodeBlock(int[] block, ref int lastDc, HuffTable dc, HuffTable ac)
        {
            // DC
            int temp = block[0] - lastDc;
            lastDc = block[0];
            int temp2 = temp;
            if (temp < 0)
            {
                temp = -temp;
                temp2--;
            }
            int nbits = temp == 0 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)temp);
            Emit(dc.Code[nbits], dc.Size[nbits]);
            if (nbits != 0) Emit((uint)temp2, nbits);

            // AC
            int r = 0;
            for (int k = 1; k < 64; k++)
            {
                temp = block[NaturalOrder[k]];
                if (temp == 0)
                {
                    r++;
                    continue;
                }
                while (r > 15)
                {
                    Emit(ac.Code[0xF0], ac.Size[0xF0]);
                    r -= 16;
                }
                temp2 = temp;
                if (temp < 0)
                {
                    temp = -temp;
                    temp2--;
                }
                nbits = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)temp);
                int i = (r << 4) + nbits;
                Emit(ac.Code[i], ac.Size[i]);
                Emit((uint)temp2, nbits);
                r = 0;
            }
            if (r > 0) Emit(ac.Code[0], ac.Size[0]); // EOB
        }

        public void Finish()
        {
            FlushBits();
            Byte(0xFF);
            Byte(0xD9); // EOI
        }

        public byte[] ToArray() => _ms.ToArray();
    }
}

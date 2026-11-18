using BitMiracle.LibJpeg.Classic;

namespace FirmReaper.Core.Imaging;

/// <summary>
/// Formato exacto del logo que ya decodifica el TV (se deduce del JPEG original del firmware): dimensiones,
/// submuestreo, tablas de cuantización e intervalo de reinicio. Todo logo nuevo se codifica igual.
/// </summary>
public sealed record LogoFormat(int Width, int Height, int LumaH, int LumaV, int[] QuantLuma, int[] QuantChroma, int RestartInterval)
{
    public string SamplingName => (LumaH, LumaV) switch { (1, 1) => "4:4:4", (2, 1) => "4:2:2", _ => "4:2:0" };

    public string Description =>
        $"{Width}x{Height}, {SamplingName}, " + (RestartInterval == 0 ? "sin reinicios" : $"reinicio cada {RestartInterval} MCU");

    /// <exception cref="InvalidDataException">El JPEG no es baseline en color con un submuestreo admitido.</exception>
    public static LogoFormat FromJpeg(byte[] jpeg)
    {
        var info = JpegInfo.Parse(jpeg);
        if (!info.IsBaseline || info.EncoderSampling is not { } s)
            throw new InvalidDataException($"El logo original no es un JPEG baseline en color 4:4:4, 4:2:2 o 4:2:0 ({info.SamplingName}).");
        if (info.ComponentQuantTable[1] != info.ComponentQuantTable[2])
            throw new InvalidDataException("El logo original usa tablas de cuantización distintas para Cb y Cr (no admitido).");
        return new LogoFormat(info.Width, info.Height, s.H, s.V,
            info.QuantTables[info.ComponentQuantTable[0]], info.QuantTables[info.ComponentQuantTable[1]], info.RestartInterval);
    }
}

/// <summary>JPEG listo para el hueco del logo y explicación de cómo se obtuvo (para el informe).</summary>
public sealed record LogoEncoding(byte[] Jpeg, string Detail);

/// <summary>
/// Operaciones sobre el logo de arranque.
/// - Decodificación: LibJpeg.NET (DCT entera "islow") + ensanchado y color como libjpeg-turbo → mismos píxeles
///   que Pillow (comprobado con los logos del Konka, ATEC y LG).
/// - Codificación: <see cref="JpegBaselineEncoder"/> (réplica de libjpeg-turbo) → mismo JPEG que Pillow.
/// Por eso girar el logo del Konka da exactamente lo que daba patch_mirror_menu.py:
///   im.convert("RGB").rotate(180).save(…, qtables=im.quantization, subsampling=0, restart_marker_blocks=171)
/// </summary>
public static class LogoImage
{
    /// <summary>Límite de píxeles para decodificar un JPEG (protege de JPEG manipulados).</summary>
    private const long MaxPixels = ImageDecoder.MaxPixels;

    /// <summary>
    /// Factores (en %) por los que se multiplican las tablas de cuantización si el logo no cabe: primero las
    /// tablas del original tal cual (100 %), después cada vez menos calidad.
    /// </summary>
    private static readonly int[] QualitySteps = [100, 125, 150, 175, 200, 250, 300, 400, 600, 1000];

    /// <summary>
    /// Decodifica a RGB (3 bytes por píxel) EXACTAMENTE como libjpeg-turbo / Pillow (API de libjpeg 6b):
    /// - LibJpeg.NET decodifica a YCbCr con la DCT entera "islow" y SIN "fancy upsampling": así la crominancia
    ///   sale a su resolución reducida (8x8, sin escalar) y solo duplicada, y se recupera exacta.
    ///   (Con "fancy", LibJpeg.NET —modelo libjpeg 7/8— escala la IDCT de la crominancia a 16x8/16x16, que NO es
    ///   lo que hace libjpeg-turbo.)
    /// - El ensanchado de la crominancia se hace aquí con el filtro triangular de jdsample.c (h2v1/h2v2) y la
    ///   conversión a RGB con las tablas de jdcolor.c.
    /// Comprobado: logos del Konka (4:4:4), ATEC (4:2:0) y LG (4:2:2) girados = mismos bytes que Pillow 12.3.0.
    /// Los JPEG en blanco y negro (1 componente) se devuelven como RGB gris.
    /// </summary>
    public static byte[] DecodeRgb(byte[] jpeg, out int width, out int height)
    {
        var info = JpegInfo.Parse(jpeg);
        if ((long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("JPEG demasiado grande.");
        if (info.Components is not (1 or 3))
            throw new InvalidDataException("El JPEG debe ser en color (YCbCr) o en blanco y negro; este parece CMYK.");
        byte[] raw;
        try
        {
            var d = new jpeg_decompress_struct();
            d.jpeg_stdio_src(new MemoryStream(jpeg, writable: false));
            d.jpeg_read_header(true);
            if (info.Components == 3 && d.Jpeg_color_space != J_COLOR_SPACE.JCS_YCbCr)
                throw new InvalidDataException("El JPEG en color no está en YCbCr (no admitido).");
            d.Out_color_space = info.Components == 3 ? J_COLOR_SPACE.JCS_YCbCr : J_COLOR_SPACE.JCS_GRAYSCALE;
            d.Dct_method = J_DCT_METHOD.JDCT_ISLOW;
            d.Do_fancy_upsampling = false;
            d.jpeg_start_decompress();
            width = d.Output_width;
            height = d.Output_height;
            int comps = d.Output_components;
            raw = new byte[width * height * comps];
            var row = jpeg_common_struct.AllocJpegSamples(width * comps, 1);
            while (d.Output_scanline < height)
            {
                int y = d.Output_scanline;
                d.jpeg_read_scanlines(row, 1);
                Array.Copy(row[0], 0, raw, y * width * comps, width * comps);
            }
            d.jpeg_finish_decompress();
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            // LibJpeg.NET lanza excepciones genéricas ante datos corruptos.
            throw new InvalidDataException("No se pudo decodificar el JPEG: " + ex.Message, ex);
        }

        if (info.Components == 1)
        {
            var g = new byte[width * height * 3];
            for (int p = 0; p < width * height; p++) g[p * 3] = g[p * 3 + 1] = g[p * 3 + 2] = raw[p];
            return g;
        }

        // Factores de la crominancia respecto a Y (Cb y Cr deben compartirlos).
        var (yh, yv) = info.Sampling[0];
        var (cbh, cbv) = info.Sampling[1];
        if (info.Sampling[2] != (cbh, cbv) || yh % cbh != 0 || yv % cbv != 0)
            throw new InvalidDataException("Submuestreo de crominancia no admitido.");
        int fh = yh / cbh, fv = yv / cbv;
        if (!((fh, fv) is (1, 1) or (2, 1) or (2, 2)))
            throw new InvalidDataException($"Submuestreo {info.SamplingName} no admitido.");

        int n = width * height;
        var Y = new byte[n];
        for (int p = 0; p < n; p++) Y[p] = raw[p * 3];
        byte[] cb, cr;
        if (fh == 1 && fv == 1)
        {
            cb = new byte[n];
            cr = new byte[n];
            for (int p = 0; p < n; p++)
            {
                cb[p] = raw[p * 3 + 1];
                cr[p] = raw[p * 3 + 2];
            }
        }
        else
        {
            cb = FancyUpsample(raw, 1, width, height, fh, fv);
            cr = FancyUpsample(raw, 2, width, height, fh, fv);
        }
        return YccToRgb(Y, cb, cr);
    }

    /// <summary>
    /// Recupera la crominancia a resolución reducida (la salida sin "fancy" la trae duplicada) y la ensancha
    /// como libjpeg-turbo: h2v1_fancy_upsample / h2v2_fancy_upsample de jdsample.c (si la anchura reducida es
    /// ≤ 2, ensanchado simple, como allí). Bordes: primera/última columna con caso especial; arriba y abajo se
    /// repite la primera/última fila reducida (filas de contexto de jdmainct.c).
    /// </summary>
    private static byte[] FancyUpsample(byte[] ycc, int comp, int width, int height, int fh, int fv)
    {
        int dw = (width + fh - 1) / fh, dh = (height + fv - 1) / fv;
        var c = new int[dw * dh];
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
                c[y * dw + x] = ycc[((y * fv) * width + x * fh) * 3 + comp];

        var outp = new byte[width * height];
        if (dw <= 2)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) outp[y * width + x] = (byte)c[(y / fv) * dw + x / fh];
            return outp;
        }
        var rowOut = new int[dw * 2];
        for (int oy = 0; oy < height; oy++)
        {
            int iy = oy / fv;
            if (fv == 1)
            {
                int o = iy * dw;
                rowOut[0] = c[o];
                rowOut[1] = (c[o] * 3 + c[o + 1] + 2) >> 2;
                for (int x = 1; x < dw - 1; x++)
                {
                    int v = c[o + x] * 3;
                    rowOut[2 * x] = (v + c[o + x - 1] + 1) >> 2;
                    rowOut[2 * x + 1] = (v + c[o + x + 1] + 2) >> 2;
                }
                int l = dw - 1;
                rowOut[2 * l] = (c[o + l] * 3 + c[o + l - 1] + 1) >> 2;
                rowOut[2 * l + 1] = c[o + l];
            }
            else
            {
                // Fila reducida más cercana (iy) y la vecina: la de arriba para la fila par, la de abajo para la impar.
                int ny = oy % 2 == 0 ? Math.Max(iy - 1, 0) : Math.Min(iy + 1, dh - 1);
                int o0 = iy * dw, o1 = ny * dw;
                int Col(int x) => c[o0 + x] * 3 + c[o1 + x];
                int cur = Col(0), next = Col(1);
                rowOut[0] = (cur * 4 + 8) >> 4;
                rowOut[1] = (cur * 3 + next + 7) >> 4;
                int last = cur;
                cur = next;
                for (int x = 1; x < dw - 1; x++)
                {
                    next = Col(x + 1);
                    rowOut[2 * x] = (cur * 3 + last + 8) >> 4;
                    rowOut[2 * x + 1] = (cur * 3 + next + 7) >> 4;
                    last = cur;
                    cur = next;
                }
                rowOut[2 * (dw - 1)] = (cur * 3 + last + 8) >> 4;
                rowOut[2 * (dw - 1) + 1] = (cur * 4 + 7) >> 4;
            }
            for (int x = 0; x < width; x++) outp[oy * width + x] = (byte)rowOut[x];
        }
        return outp;
    }

    // jdcolor.c: tablas de YCbCr → RGB (enteras, SCALEBITS = 16).
    private static readonly int[] CrR = new int[256], CbB = new int[256], CrG = new int[256], CbG = new int[256];

    static LogoImage()
    {
        const int sb = 16;
        static int Fix(double x) => (int)(x * (1 << sb) + 0.5);
        for (int i = 0; i < 256; i++)
        {
            int x = i - 128;
            CrR[i] = (Fix(1.40200) * x + (1 << (sb - 1))) >> sb;
            CbB[i] = (Fix(1.77200) * x + (1 << (sb - 1))) >> sb;
            CrG[i] = -Fix(0.71414) * x;
            CbG[i] = -Fix(0.34414) * x + (1 << (sb - 1));
        }
    }

    private static byte[] YccToRgb(byte[] y, byte[] cb, byte[] cr)
    {
        var rgb = new byte[y.Length * 3];
        for (int p = 0; p < y.Length; p++)
        {
            int Y = y[p], b = cb[p], r = cr[p];
            rgb[p * 3] = (byte)Math.Clamp(Y + CrR[r], 0, 255);
            rgb[p * 3 + 1] = (byte)Math.Clamp(Y + ((CbG[b] + CrG[r]) >> 16), 0, 255);
            rgb[p * 3 + 2] = (byte)Math.Clamp(Y + CbB[b], 0, 255);
        }
        return rgb;
    }

    /// <summary>Gira 180° (equivale a voltear horizontal y verticalmente).</summary>
    public static RgbImage Rotate180(RgbImage img)
    {
        var src = img.Pixels;
        var rot = new byte[src.Length];
        int n = img.Width * img.Height;
        for (int p = 0; p < n; p++)
        {
            int q = (n - 1 - p) * 3;
            rot[q] = src[p * 3];
            rot[q + 1] = src[p * 3 + 1];
            rot[q + 2] = src[p * 3 + 2];
        }
        return img with { Pixels = rot };
    }

    /// <summary>
    /// Gira el logo original 180° y lo vuelve a codificar en su mismo formato (tablas, submuestreo, reinicios).
    /// </summary>
    public static LogoEncoding Rotated(byte[] originalJpeg, int maxBytes)
    {
        var fmt = LogoFormat.FromJpeg(originalJpeg);
        var rgb = DecodeRgb(originalJpeg, out int w, out int h);
        var enc = EncodeToFit(Rotate180(new RgbImage(rgb, w, h)), fmt, maxBytes);
        return enc with { Detail = "Logo original girado 180°; " + enc.Detail };
    }

    /// <summary>
    /// Prepara el logo propio a partir de la imagen del usuario (JPEG, PNG o BMP de cualquier tamaño):
    /// - si ya es un JPEG con el formato exacto y cabe, se usa TAL CUAL (sin recomprimir);
    /// - si no, se decodifica, se encaja en el tamaño del logo sin deformarla (bandas negras si la proporción
    ///   es distinta) y se codifica en el formato del logo original, bajando la calidad solo si no cabe.
    /// </summary>
    public static LogoEncoding FromUserImage(byte[] file, LogoFormat fmt, int maxBytes)
    {
        if (file.Length >= 3 && file[0] == 0xFF && file[1] == 0xD8 && file[2] == 0xFF &&
            WhyNotDirect(file, fmt, maxBytes) is null)
            return new LogoEncoding(file, $"JPEG del usuario usado tal cual (ya tenía el formato {fmt.Description}).");

        var img = ImageDecoder.Decode(file);
        var fitted = FitInto(img, fmt.Width, fmt.Height);
        var enc = EncodeToFit(fitted, fmt, maxBytes);
        string how = img.Width == fmt.Width && img.Height == fmt.Height
            ? $"imagen de {img.Width}x{img.Height}"
            : $"imagen de {img.Width}x{img.Height} encajada en {fmt.Width}x{fmt.Height} sin deformar" +
              ((long)img.Width * fmt.Height == (long)img.Height * fmt.Width ? "" : " (bandas negras)");
        return enc with { Detail = $"Logo propio: {how}; {enc.Detail}" };
    }

    /// <summary>
    /// Null si el JPEG se puede escribir tal cual en el hueco del logo; si no, el motivo.
    /// Exige: SOF0 (baseline), en color, mismas dimensiones y submuestreo que el original, nada detrás del
    /// final (FFD9), que quepa y que se decodifique entero.
    /// </summary>
    public static string? WhyNotDirect(byte[] jpeg, LogoFormat fmt, int maxBytes)
    {
        JpegInfo info;
        try
        {
            info = JpegInfo.Parse(jpeg);
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
        if (info.SofMarker != 0xC0) return "no es JPEG baseline (SOF0)";
        if (info.Components != 3) return "no es en color";
        if (info.Width != fmt.Width || info.Height != fmt.Height) return $"mide {info.Width}x{info.Height} (se necesita {fmt.Width}x{fmt.Height})";
        if (info.EncoderSampling != (fmt.LumaH, fmt.LumaV)) return $"submuestreo {info.SamplingName} (se necesita {fmt.SamplingName})";
        if (info.Length != jpeg.Length) return "hay datos después del final del JPEG (FFD9)";
        if (jpeg.Length > maxBytes) return $"ocupa {Fmt.N(jpeg.Length)} bytes (máximo {Fmt.N(maxBytes)})";
        try
        {
            DecodeRgb(jpeg, out _, out _);
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
        return null;
    }

    /// <summary>
    /// Codifica en el formato del logo. Empieza con las tablas del original; si no cabe en
    /// <paramref name="maxBytes"/>, las multiplica por factores crecientes (menos calidad) y, si aun así no cabe,
    /// usa las tablas estándar JPEG (anexo K) a calidades 50, 25, 10, 5 y 1, como jpeg_set_quality de libjpeg.
    /// </summary>
    /// <exception cref="InvalidDataException">No cabe ni con la calidad mínima.</exception>
    public static LogoEncoding EncodeToFit(RgbImage img, LogoFormat fmt, int maxBytes)
    {
        if (img.Width != fmt.Width || img.Height != fmt.Height)
            throw new ArgumentException("La imagen no tiene el tamaño del logo.", nameof(img));
        var steps = QualitySteps.Select(pct => (Scale(fmt.QuantLuma, pct), Scale(fmt.QuantChroma, pct),
                pct == 100
                    ? "codificado con las tablas de cuantización del logo original"
                    : $"codificado con calidad reducida (tablas del original ×{pct / 100.0:0.##}) para caber"))
            .Concat(StandardQualities.Select(q => (StdTable(StdLuma, q), StdTable(StdChroma, q),
                $"codificado con calidad reducida (tablas JPEG estándar, calidad {q}) para caber")));
        int last = 0;
        foreach (var (ql, qc, how) in steps)
        {
            var j = JpegBaselineEncoder.Encode(img.Pixels, img.Width, img.Height, ql, qc, fmt.RestartInterval, fmt.LumaH, fmt.LumaV);
            last = j.Length;
            if (j.Length <= maxBytes)
                return new LogoEncoding(j, $"{how}; {fmt.Description}; {Fmt.N(j.Length)} de {Fmt.N(maxBytes)} bytes.");
        }
        throw new InvalidDataException(
            $"La imagen no cabe en el hueco del logo ni con la calidad mínima ({Fmt.N(last)} bytes; máximo {Fmt.N(maxBytes)}). " +
            "Use una imagen más sencilla (por ejemplo, logo sobre fondo liso).");
    }

    private static int[] Scale(int[] table, int pct) =>
        pct == 100 ? table : table.Select(q => Math.Clamp((q * pct + 50) / 100, 1, 255)).ToArray();

    private static readonly int[] StandardQualities = [50, 25, 10, 5, 1];

    // Tablas del anexo K de la norma JPEG (orden natural), las de jcparam.c.
    private static readonly int[] StdLuma =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62, 18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static readonly int[] StdChroma =
    [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    ];

    /// <summary>jpeg_quality_scaling + jpeg_add_quant_table (baseline: valores 1..255).</summary>
    private static int[] StdTable(int[] std, int quality)
    {
        int scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        return std.Select(v => Math.Clamp((v * scale + 50) / 100, 1, 255)).ToArray();
    }

    /// <summary>
    /// Encaja la imagen en <paramref name="width"/>x<paramref name="height"/> conservando la proporción,
    /// centrada y con bandas negras. Reducción: media por área; ampliación: bilineal. Aritmética determinista.
    /// </summary>
    public static RgbImage FitInto(RgbImage src, int width, int height)
    {
        if (src.Width == width && src.Height == height) return src;
        double scale = Math.Min((double)width / src.Width, (double)height / src.Height);
        int dw = Math.Clamp((int)Math.Round(src.Width * scale, MidpointRounding.AwayFromZero), 1, width);
        int dh = Math.Clamp((int)Math.Round(src.Height * scale, MidpointRounding.AwayFromZero), 1, height);
        var scaled = Resize(src, dw, dh);
        var outPix = new byte[width * height * 3];
        int ox = (width - dw) / 2, oy = (height - dh) / 2;
        for (int y = 0; y < dh; y++)
            Array.Copy(scaled.Pixels, y * dw * 3, outPix, ((oy + y) * width + ox) * 3, dw * 3);
        return new RgbImage(outPix, width, height);
    }

    /// <summary>Pesos de un eje: para cada destino, primer índice de origen y pesos (suman 1).</summary>
    private static (int First, double[] W)[] AxisWeights(int srcLen, int dstLen)
    {
        var res = new (int, double[])[dstLen];
        double r = (double)srcLen / dstLen;
        for (int i = 0; i < dstLen; i++)
        {
            if (r >= 1)
            {
                // Media por área: [i·r, (i+1)·r)
                double a = i * r, b = (i + 1) * r;
                int first = (int)Math.Floor(a), last = Math.Min(srcLen - 1, (int)Math.Ceiling(b) - 1);
                var w = new double[last - first + 1];
                for (int k = first; k <= last; k++) w[k - first] = Math.Min(b, k + 1) - Math.Max(a, k);
                Normalize(w);
                res[i] = (first, w);
            }
            else
            {
                // Bilineal con centros de píxel alineados.
                double x = (i + 0.5) * r - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(x), 0, srcLen - 1);
                int x1 = Math.Min(x0 + 1, srcLen - 1);
                double t = Math.Clamp(x - x0, 0, 1);
                res[i] = x1 == x0 ? (x0, [1.0]) : (x0, [1 - t, t]);
            }
        }
        return res;
    }

    private static void Normalize(double[] w)
    {
        double s = w.Sum();
        for (int k = 0; k < w.Length; k++) w[k] /= s;
    }

    private static RgbImage Resize(RgbImage src, int dw, int dh)
    {
        if (src.Width == dw && src.Height == dh) return src;
        var wx = AxisWeights(src.Width, dw);
        var wy = AxisWeights(src.Height, dh);
        var outPix = new byte[dw * dh * 3];
        // Filas de origen ya reescaladas en horizontal; solo se guardan las que aún pueden necesitarse.
        var cache = new Dictionary<int, double[]>();
        double[] Row(int sy)
        {
            if (cache.TryGetValue(sy, out var r)) return r;
            r = new double[dw * 3];
            int so = sy * src.Width * 3;
            for (int x = 0; x < dw; x++)
            {
                var (first, w) = wx[x];
                double cr = 0, cg = 0, cb = 0;
                for (int k = 0; k < w.Length; k++)
                {
                    int p = so + (first + k) * 3;
                    cr += src.Pixels[p] * w[k];
                    cg += src.Pixels[p + 1] * w[k];
                    cb += src.Pixels[p + 2] * w[k];
                }
                r[x * 3] = cr;
                r[x * 3 + 1] = cg;
                r[x * 3 + 2] = cb;
            }
            cache[sy] = r;
            return r;
        }
        for (int y = 0; y < dh; y++)
        {
            var (first, w) = wy[y];
            foreach (int old in cache.Keys.Where(k => k < first).ToList()) cache.Remove(old);
            var acc = new double[dw * 3];
            for (int k = 0; k < w.Length; k++)
            {
                var r = Row(first + k);
                for (int i = 0; i < acc.Length; i++) acc[i] += r[i] * w[k];
            }
            for (int i = 0; i < acc.Length; i++)
                outPix[y * dw * 3 + i] = (byte)Math.Clamp((int)Math.Round(acc[i], MidpointRounding.AwayFromZero), 0, 255);
        }
        return new RgbImage(outPix, dw, dh);
    }
}

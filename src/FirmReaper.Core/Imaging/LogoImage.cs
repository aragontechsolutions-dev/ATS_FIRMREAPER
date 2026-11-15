using BitMiracle.LibJpeg.Classic;

namespace FirmReaper.Core.Imaging;

/// <summary>
/// Operaciones sobre el logo de arranque (JPEG). Reproduce exactamente lo que hacía
/// patch_mirror_menu.py con Pillow:
///   im.convert("RGB").rotate(180).save(…, qtables=im.quantization, subsampling=0,
///                                    optimize=False, progressive=False, restart_marker_blocks=171)
/// - Decodificación: LibJpeg.NET (port de libjpeg, DCT entera "islow") → mismos píxeles que Pillow
///   (comprobado byte a byte con el logo del Konka).
/// - Codificación: <see cref="JpegBaselineEncoder"/> (réplica de libjpeg-turbo) → mismo JPEG que Pillow.
/// </summary>
public static class LogoImage
{
    /// <summary>Límite de píxeles para decodificar (protege de JPEG manipulados).</summary>
    private const long MaxPixels = 4096L * 4096;

    /// <summary>Decodifica a RGB (3 bytes por píxel).</summary>
    public static byte[] DecodeRgb(byte[] jpeg, out int width, out int height)
    {
        var info = JpegInfo.Parse(jpeg);
        if ((long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("JPEG demasiado grande.");
        try
        {
            var d = new jpeg_decompress_struct();
            d.jpeg_stdio_src(new MemoryStream(jpeg, writable: false));
            d.jpeg_read_header(true);
            d.Out_color_space = J_COLOR_SPACE.JCS_RGB;
            d.Dct_method = J_DCT_METHOD.JDCT_ISLOW;
            d.jpeg_start_decompress();
            width = d.Output_width;
            height = d.Output_height;
            if (d.Output_components != 3)
                throw new InvalidDataException("El logo debe ser en color (3 componentes).");
            var pix = new byte[width * height * 3];
            var row = jpeg_common_struct.AllocJpegSamples(width * 3, 1);
            while (d.Output_scanline < height)
            {
                int y = d.Output_scanline;
                d.jpeg_read_scanlines(row, 1);
                Array.Copy(row[0], 0, pix, y * width * 3, width * 3);
            }
            d.jpeg_finish_decompress();
            return pix;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            // LibJpeg.NET lanza excepciones genéricas ante datos corruptos.
            throw new InvalidDataException("No se pudo decodificar el JPEG: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Gira el JPEG 180° y lo vuelve a codificar con las mismas tablas de cuantización, 4:4:4 y el
    /// mismo intervalo de reinicio que el original.
    /// </summary>
    public static byte[] Rotate180(byte[] jpeg)
    {
        var info = JpegInfo.Parse(jpeg);
        if (!info.IsBaseline || info.Components != 3 || !info.Is444)
            throw new InvalidDataException("Solo se gira un JPEG baseline en color 4:4:4 (como el logo original).");
        if (info.ComponentQuantTable.Count != 3 || info.ComponentQuantTable[0] != 0 ||
            info.ComponentQuantTable[1] != 1 || info.ComponentQuantTable[2] != 1)
            throw new InvalidDataException("Distribución de tablas de cuantización no soportada (se espera Y=0, Cb=Cr=1).");

        var rgb = DecodeRgb(jpeg, out int w, out int h);
        var rot = new byte[rgb.Length];
        int n = w * h;
        for (int p = 0; p < n; p++)
        {
            int q = (n - 1 - p) * 3;
            rot[q] = rgb[p * 3];
            rot[q + 1] = rgb[p * 3 + 1];
            rot[q + 2] = rgb[p * 3 + 2];
        }
        return JpegBaselineEncoder.Encode(rot, w, h, info.QuantTables[0], info.QuantTables[1], info.RestartInterval);
    }
}

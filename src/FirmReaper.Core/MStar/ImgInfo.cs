using System.Buffers.Binary;
using System.Text;

namespace FirmReaper.Core.MStar;

/// <summary>
/// Bloque MS_IMG_INFO en 0x21180 (magic 78 56 AA 55). Solo se leen los campos verificados:
/// - Textos "DVBT…" y "PANEL_NAME" (informativos).
/// - Marca de compresión 0xDEADBE (deflate) / 0xDEADEF (LZMA) seguida de un u32 LE con
///   la longitud de la app en flash (= FlashEnd − 0x20080). Verificado en los 3 binarios analizados.
/// AVISO: los scripts Python NO actualizan este campo al parchear y los firmwares funcionaron en el TV;
/// por eso una discrepancia aquí es solo una advertencia, no un error.
/// </summary>
public sealed record ImgInfo(int MarkerOffset, uint CompressionMagic, uint AppFlashLength, string Texts)
{
    private const int SearchLength = 0x180;

    public bool IsDeflate => CompressionMagic == 0xDEADBE;
    public bool IsLzma => CompressionMagic == 0xDEADEF;

    public static ImgInfo? TryRead(ReadOnlySpan<byte> fw)
    {
        int o = MStarLayout.ImgInfoOffset;
        if (fw.Length < o + SearchLength || !fw.Slice(o, 4).SequenceEqual(MStarLayout.ImgInfoMagic))
            return null;
        var area = fw.Slice(o, SearchLength);
        int rel = IndexOfMarker(area);
        if (rel < 0 || rel + 7 > area.Length)
            return null;
        uint magic = (uint)(area[rel] | area[rel + 1] << 8 | area[rel + 2] << 16);
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(area[(rel + 3)..]);
        return new ImgInfo(o + rel, magic, len, ExtractTexts(area[..rel]));
    }

    private static int IndexOfMarker(ReadOnlySpan<byte> area)
    {
        int a = area.IndexOf([(byte)0xBE, (byte)0xAD, (byte)0xDE]);
        int b = area.IndexOf([(byte)0xEF, (byte)0xAD, (byte)0xDE]);
        if (a < 0) return b;
        if (b < 0) return a;
        return Math.Min(a, b);
    }

    private static string ExtractTexts(ReadOnlySpan<byte> area)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        foreach (byte c in area)
        {
            if (c >= 0x20 && c < 0x7F) { sb.Append((char)c); continue; }
            if (sb.Length >= 4) parts.Add(sb.ToString());
            sb.Clear();
        }
        if (sb.Length >= 4) parts.Add(sb.ToString());
        return string.Join(" | ", parts);
    }
}

namespace FirmReaper.Core.MStar;

/// <summary>
/// Constantes del formato SPI MStar (placas sin Android, CPU AEON R2) confirmadas en los binarios
/// analizados (ATEC 32L18D, LG 42LF652VX, Soyea) y coherentes con el SDK Chakra3 de MStar
/// (BinIDPackFiles_Compress.py / PadLoaderCrc.py). Ver docs/FORMATO_MSTAR.md.
/// </summary>
public static class MStarLayout
{
    /// <summary>Tamaño de la memoria SPI soportada (4 MB).</summary>
    public const int FlashSize = 0x400000;

    /// <summary>Cabecera de la aplicación ("chunk header"): 6 × u32 little-endian.</summary>
    public const int HeaderOffset = 0x020000;

    /// <summary>Inicio de la aplicación en flash (valor esperado en cabecera +0x00).</summary>
    public const int AppFlashStart = 0x020080;

    /// <summary>Primeros bytes de la app sin comprimir (mscompress -u 14336).</summary>
    public const int UncompressedPrefix = 0x3800;

    /// <summary>Inicio del deflate crudo = AppFlashStart + UncompressedPrefix.</summary>
    public const int DeflateOffset = AppFlashStart + UncompressedPrefix; // 0x023880

    /// <summary>Tabla TEL de recursos (magic "TEL\x09X3i\0").</summary>
    public const int TelOffset = 0x022080;

    /// <summary>Bloque MS_IMG_INFO (magic 78 56 AA 55).</summary>
    public const int ImgInfoOffset = 0x021180;

    /// <summary>Cola tras el deflate: u32 LE tamaño descomprimido + 2 bytes (función desconocida).</summary>
    public const int TailSize = 6;

    /// <summary>Máximo admitido para la app descomprimida (protección ante datos corruptos o maliciosos).</summary>
    public const int MaxAppSize = 32 * 1024 * 1024;

    public static ReadOnlySpan<byte> TelMagic => "TEL\tX3i\0"u8;
    public static ReadOnlySpan<byte> ImgInfoMagic => [0x78, 0x56, 0xAA, 0x55];
}

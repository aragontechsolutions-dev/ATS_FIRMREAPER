using System.Buffers.Binary;

namespace FirmReaper.Core.MStar;

/// <summary>
/// Cabecera de la aplicación en 0x20000 (6 × u32 little-endian).
/// </summary>
/// <param name="FlashStart">+0x00 inicio de la app en flash (0x20080).</param>
/// <param name="RamDest">+0x04 dirección de carga en RAM.</param>
/// <param name="RamEnd">+0x08 fin en RAM = RamDest + (FlashEnd − FlashStart).</param>
/// <param name="FlashEnd">+0x0C fin en flash (= fin del deflate + 6 bytes de cola).</param>
/// <param name="Reserved">+0x10 (0 en todos los binarios vistos).</param>
/// <param name="Crc">+0x14 CRC-32 no reflejado de flash[FlashStart..FlashEnd).</param>
public sealed record AppHeader(uint FlashStart, uint RamDest, uint RamEnd, uint FlashEnd, uint Reserved, uint Crc)
{
    public const int Size = 24;

    public static AppHeader Read(ReadOnlySpan<byte> fw)
    {
        var h = fw.Slice(MStarLayout.HeaderOffset, Size);
        return new AppHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x00..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x04..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x08..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x0C..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x14..]));
    }

    /// <summary>Tamaño de la app en flash (parte sin comprimir + deflate + cola).</summary>
    public long FlashLength => (long)FlashEnd - FlashStart;
}

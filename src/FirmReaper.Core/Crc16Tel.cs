using System.Buffers.Binary;

namespace FirmReaper.Core;

/// <summary>
/// CRC-16 de los recursos de la tabla TEL: los 2 ÚLTIMOS bytes de cada recurso (big-endian) son el CRC de
/// todo lo anterior. Polinomio 0x8005, valor inicial 0, sin reflejar, sin XOR final ("CRC-16/UMTS").
/// Comprobado en los 41 recursos de los 4 firmwares originales (ATEC, LG, Soyea, Konka).
/// El arranque de los firmwares conocidos NO lo comprueba (la búsqueda de recursos solo lee la tabla),
/// pero el programa lo mantiene correcto al cambiar un recurso.
/// </summary>
public static class Crc16Tel
{
    public const ushort Polynomial = 0x8005;

    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var t = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int c = i << 8;
            for (int k = 0; k < 8; k++)
                c = (c & 0x8000) != 0 ? (c << 1) ^ Polynomial : c << 1;
            t[i] = (ushort)c;
        }
        return t;
    }

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        var t = Table;
        ushort crc = 0;
        foreach (byte b in data)
            crc = (ushort)((crc << 8) ^ t[((crc >> 8) ^ b) & 0xFF]);
        return crc;
    }

    /// <summary>True si los 2 últimos bytes del recurso son el CRC de los anteriores.</summary>
    public static bool IsValid(ReadOnlySpan<byte> resource) =>
        resource.Length >= 2 &&
        Compute(resource[..^2]) == BinaryPrimitives.ReadUInt16BigEndian(resource[^2..]);

    /// <summary>Escribe en los 2 últimos bytes el CRC de los anteriores.</summary>
    public static void Update(Span<byte> resource)
    {
        if (resource.Length < 2) throw new ArgumentException("Recurso demasiado corto.", nameof(resource));
        BinaryPrimitives.WriteUInt16BigEndian(resource[^2..], Compute(resource[..^2]));
    }
}

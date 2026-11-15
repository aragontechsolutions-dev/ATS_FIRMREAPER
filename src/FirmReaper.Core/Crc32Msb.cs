namespace FirmReaper.Core;

/// <summary>
/// CRC-32 NO reflejado (MSB primero) usado por MStar en la cabecera de la aplicación:
/// polinomio 0x04C11DB7, valor inicial 0, sin XOR final.
/// Es el mismo que <c>crcmod.mkCrcFun(0x104C11DB7, 0, 0)</c> del SDK y que <c>crc32_nr</c> de los scripts Python.
/// </summary>
public static class Crc32Msb
{
    public const uint Polynomial = 0x04C11DB7;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i << 24;
            for (int k = 0; k < 8; k++)
                c = (c & 0x8000_0000) != 0 ? (c << 1) ^ Polynomial : c << 1;
            t[i] = c;
        }
        return t;
    }

    /// <summary>Calcula el CRC sobre <paramref name="data"/>, continuando desde <paramref name="crc"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data, uint crc = 0)
    {
        var t = Table;
        foreach (byte b in data)
            crc = (crc << 8) ^ t[((crc >> 24) ^ b) & 0xFF];
        return crc;
    }
}

using System.Buffers.Binary;

namespace FirmReaper.Core.MStar;

/// <summary>Una entrada de la tabla TEL de recursos (11 bytes, big-endian).</summary>
/// <param name="Id">Identificador del recurso (BIN_ID_*; p. ej. 0x0300/0x0301 = logo JPEG).</param>
/// <param name="Offset">Dirección en flash.</param>
/// <param name="Length">Longitud en bytes.</param>
/// <param name="Flags">0x01 LZSS, 0x04 deflate, 0x08 LZMA (según el SDK).</param>
/// <param name="Content">Tipo de contenido deducido de la firma de los primeros bytes (informativo).</param>
public sealed record TelEntry(ushort Id, uint Offset, uint Length, byte Flags, string Content)
{
    public string Compression => Flags switch
    {
        0x00 => "sin comprimir",
        0x01 => "LZSS",
        0x04 => "deflate",
        0x08 => "LZMA",
        _ => $"0x{Flags:X2}",
    };

    /// <summary>Detecta el tipo de contenido por la firma de los primeros bytes (solo informativo).</summary>
    public static string GuessContent(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "JPEG";
        if (head.Length >= 3 && head[0] == 0x5D && head[1] == 0x00 && head[2] == 0x00) return "LZMA";
        if (head.StartsWith("ID3"u8)) return "MP3 (ID3)";
        if (head.StartsWith("MST"u8)) return "MST";
        return "";
    }
}

/// <summary>
/// Tabla TEL en 0x22080: magic "TEL\x09X3i\0", u32 BE fin del área de recursos, u16 BE nº de entradas,
/// entradas de 11 bytes y una entrada final de id 0xF000 (terminador). Cada recurso termina con un CRC-16
/// (ver <see cref="Crc16Tel"/>).
/// </summary>
/// <param name="ReservedSize">
/// Dirección de flash donde termina el área de recursos (0x3C0000 en ATEC, Soyea y Konka; 0x370000 en el LG,
/// donde empieza la zona escribible por el propio TV con el logo capturado y los ajustes).
/// </param>
public sealed record TelTable(uint ReservedSize, IReadOnlyList<TelEntry> Entries, bool HasTerminator)
{
    public const int EntrySize = 11;
    public const ushort TerminatorId = 0xF000;
    private const int MaxEntries = 256;

    /// <summary>Primer byte de flash ocupado por un recurso: la app no puede pasar de aquí.</summary>
    public uint FirstResourceOffset => Entries.Count == 0 ? MStarLayout.FlashSize : Entries.Min(e => e.Offset);

    /// <summary>Lee y valida la tabla. Devuelve null y un motivo si no es válida.</summary>
    public static TelTable? TryRead(ReadOnlySpan<byte> fw, out string error)
    {
        int o = MStarLayout.TelOffset;
        if (fw.Length < o + 14 || !fw.Slice(o, 8).SequenceEqual(MStarLayout.TelMagic))
        {
            error = "no se encontró la firma TEL en 0x22080";
            return null;
        }
        uint reserved = BinaryPrimitives.ReadUInt32BigEndian(fw[(o + 8)..]);
        int n = BinaryPrimitives.ReadUInt16BigEndian(fw[(o + 12)..]);
        if (n == 0 || n > MaxEntries)
        {
            error = $"número de entradas TEL fuera de rango ({n})";
            return null;
        }
        int p = o + 14;
        if (p + (n + 1) * EntrySize > fw.Length)
        {
            error = "la tabla TEL sale del archivo";
            return null;
        }
        var list = new List<TelEntry>(n);
        for (int i = 0; i < n; i++, p += EntrySize)
        {
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(fw[p..]);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(fw[(p + 2)..]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(fw[(p + 6)..]);
            if ((ulong)offset + length > (ulong)fw.Length)
            {
                error = $"el recurso 0x{id:X4} sale del archivo (0x{offset:X}+0x{length:X})";
                return null;
            }
            var head = fw.Slice((int)offset, (int)Math.Min(length, 8u));
            list.Add(new TelEntry(id, offset, length, fw[p + 10], TelEntry.GuessContent(head)));
        }
        bool term = BinaryPrimitives.ReadUInt16BigEndian(fw[p..]) == TerminatorId;
        error = "";
        return new TelTable(reserved, list, term);
    }
}

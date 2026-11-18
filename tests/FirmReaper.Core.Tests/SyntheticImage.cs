using System.Buffers.Binary;
using System.IO.Compression;
using FirmReaper.Core.MStar;

namespace FirmReaper.Core.Tests;

/// <summary>
/// Construye una imagen MStar SINTÉTICA de 4 MB con la misma estructura que las reales
/// (cabecera + CRC, MS_IMG_INFO, TEL, deflate + cola). Permite probar el motor sin usar
/// firmwares del fabricante (que no se suben al repositorio).
/// </summary>
internal static class SyntheticImage
{
    public const uint RamDest = 0x240000;
    public const uint FirstResource = 0x200000;

    /// <param name="firstResource">Offset del primer recurso TEL (límite de la app). Por defecto 0x200000.</param>
    public static byte[] Build(int appSize = 300_000, int seed = 1, uint firstResource = FirstResource)
    {
        var fw = new byte[MStarLayout.FlashSize];
        var rnd = new Random(seed);

        // App: los 0x3800 primeros bytes van sin comprimir en flash (stub), el resto se comprime.
        var stub = new byte[MStarLayout.UncompressedPrefix];
        rnd.NextBytes(stub);
        stub.CopyTo(fw, MStarLayout.AppFlashStart);

        var app = new byte[appSize];
        for (int i = 0; i < app.Length; i++) app[i] = (byte)((i % 251) ^ (i >> 10));
        byte[] comp;
        using (var ms = new MemoryStream())
        {
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) ds.Write(app);
            comp = ms.ToArray();
        }
        comp.CopyTo(fw, MStarLayout.DeflateOffset);
        int end = MStarLayout.DeflateOffset + comp.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(end), (uint)app.Length);
        fw[end + 4] = 0xF8;
        fw[end + 5] = 0x71;
        uint flashEnd = (uint)(end + 6);

        // MS_IMG_INFO (se escribe ANTES del CRC porque está dentro de la zona cubierta)
        int ii = MStarLayout.ImgInfoOffset;
        MStarLayout.ImgInfoMagic.CopyTo(fw.AsSpan(ii));
        "DVBT-----\0PANEL_NAME\0"u8.CopyTo(fw.AsSpan(ii + 0x1E));
        fw[ii + 0x44] = 0xBE; fw[ii + 0x45] = 0xAD; fw[ii + 0x46] = 0xDE;
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(ii + 0x47), flashEnd - MStarLayout.AppFlashStart);

        // TEL con 2 recursos (uno JPEG) y terminador
        int t = MStarLayout.TelOffset;
        MStarLayout.TelMagic.CopyTo(fw.AsSpan(t));
        BinaryPrimitives.WriteUInt32BigEndian(fw.AsSpan(t + 8), 0x3C0000);
        BinaryPrimitives.WriteUInt16BigEndian(fw.AsSpan(t + 12), 2);
        WriteTel(fw, t + 14, 0x0061, firstResource, 0x1000, 0x04);
        WriteTel(fw, t + 25, 0x0300, firstResource + 0x1000, 0x100, 0x00);
        WriteTel(fw, t + 36, 0xF000, 0xCCCCCCCC, 0xCCCCCCCC, 0xCC);
        fw[firstResource + 0x1000] = 0xFF; fw[firstResource + 0x1001] = 0xD8; fw[firstResource + 0x1002] = 0xFF;
        // Como en los reales: cada recurso termina con su CRC-16.
        Crc16Tel.Update(fw.AsSpan((int)firstResource, 0x1000));
        Crc16Tel.Update(fw.AsSpan((int)firstResource + 0x1000, 0x100));

        if (flashEnd > firstResource)
            throw new ArgumentException("la app sintética no cabe antes del primer recurso");
        WriteHeader(fw, flashEnd);
        return fw;
    }

    public static void WriteTel(byte[] fw, int p, ushort id, uint off, uint len, byte flags)
    {
        BinaryPrimitives.WriteUInt16BigEndian(fw.AsSpan(p), id);
        BinaryPrimitives.WriteUInt32BigEndian(fw.AsSpan(p + 2), off);
        BinaryPrimitives.WriteUInt32BigEndian(fw.AsSpan(p + 6), len);
        fw[p + 10] = flags;
    }

    /// <summary>Escribe la cabecera con el CRC correcto para el contenido actual.</summary>
    public static void WriteHeader(byte[] fw, uint flashEnd)
    {
        var h = fw.AsSpan(MStarLayout.HeaderOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(h, MStarLayout.AppFlashStart);
        BinaryPrimitives.WriteUInt32LittleEndian(h[4..], RamDest);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], RamDest + (flashEnd - MStarLayout.AppFlashStart));
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], flashEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], 0);
        RefreshCrc(fw);
    }

    public static void RefreshCrc(byte[] fw)
    {
        uint flashEnd = BinaryPrimitives.ReadUInt32LittleEndian(fw.AsSpan(MStarLayout.HeaderOffset + 12));
        uint crc = Crc32Msb.Compute(fw.AsSpan(MStarLayout.AppFlashStart, (int)(flashEnd - MStarLayout.AppFlashStart)));
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(MStarLayout.HeaderOffset + 20), crc);
    }

    public static uint FlashEnd(byte[] fw) => BinaryPrimitives.ReadUInt32LittleEndian(fw.AsSpan(MStarLayout.HeaderOffset + 12));
}

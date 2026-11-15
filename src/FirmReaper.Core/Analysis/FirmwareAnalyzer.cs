using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FirmReaper.Core.Compression;
using FirmReaper.Core.MStar;

namespace FirmReaper.Core.Analysis;

/// <summary>
/// Analiza un volcado SPI MStar de 4 MB SIN modificarlo.
/// Trata el archivo como dato no confiable: cada offset leído del propio archivo se valida antes de usarse.
/// </summary>
public static class FirmwareAnalyzer
{
    /// <summary>Lee el archivo (solo lectura, tamaño limitado) y lo analiza.</summary>
    public static FirmwareAnalysis AnalyzeFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("No existe el archivo", path);
        if (info.Length != MStarLayout.FlashSize)
        {
            // No se lee el contenido de archivos de otro tamaño (evita cargar archivos enormes en memoria).
            return new FirmwareAnalysis
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                Sha256 = HashFile(info.FullName),
                Checks =
                [
                    new Check("size", "Tamaño de 4 MB", CheckStatus.Error,
                        $"El archivo mide {Fmt.N(info.Length)} bytes; se esperaban {Fmt.N(MStarLayout.FlashSize)} (SPI de 4 MB)."),
                ],
            };
        }
        byte[] fw;
        using (var fs = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fw = new byte[MStarLayout.FlashSize];
            fs.ReadExactly(fw);
        }
        return Analyze(fw, info.FullName);
    }

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    public static FirmwareAnalysis Analyze(byte[] fw, string filePath = "")
    {
        ArgumentNullException.ThrowIfNull(fw);
        var checks = new List<Check>();
        string sha = Convert.ToHexStringLower(SHA256.HashData(fw));

        FirmwareAnalysis Result(AppHeader? h = null, uint? crc = null, TelTable? tel = null, ImgInfo? img = null,
            int? defEnd = null, byte[]? tail = null, byte[]? app = null, long? free = null) => new()
        {
            FilePath = filePath,
            FileSize = fw.Length,
            Sha256 = sha,
            Header = h,
            ComputedCrc = crc,
            Tel = tel,
            ImgInfo = img,
            DeflateEnd = defEnd,
            Tail = tail,
            App = app,
            AppSize = app?.Length,
            FreeBytes = free,
            BoardId = FindAscii(fw, "BD_"u8, 0, MStarLayout.HeaderOffset),
            SbootTag = FindTagAround(fw, "_SBOOT"u8, 0, MStarLayout.HeaderOffset),
            Checks = checks,
        };

        // 1. Tamaño
        if (fw.Length != MStarLayout.FlashSize)
        {
            checks.Add(new Check("size", "Tamaño de 4 MB", CheckStatus.Error,
                $"El archivo mide {Fmt.N(fw.Length)} bytes; se esperaban {Fmt.N(MStarLayout.FlashSize)}."));
            return Result();
        }
        checks.Add(new Check("size", "Tamaño de 4 MB", CheckStatus.Ok, $"{Fmt.N(MStarLayout.FlashSize)} bytes"));

        // 2. Cabecera
        var h = AppHeader.Read(fw);
        if (h.FlashStart != MStarLayout.AppFlashStart)
        {
            checks.Add(new Check("hdr.start", "Inicio de la app en la cabecera", CheckStatus.Error,
                $"Cabecera +0x00 = 0x{h.FlashStart:X8}; se esperaba 0x{MStarLayout.AppFlashStart:X8}. Formato no soportado."));
            return Result(h);
        }
        checks.Add(new Check("hdr.start", "Inicio de la app en la cabecera", CheckStatus.Ok, $"0x{h.FlashStart:X8}"));

        if (h.FlashEnd <= MStarLayout.DeflateOffset + MStarLayout.TailSize || h.FlashEnd > fw.Length)
        {
            checks.Add(new Check("hdr.end", "Fin de la app en flash", CheckStatus.Error,
                $"Cabecera +0x0C = 0x{h.FlashEnd:X8} fuera de rango."));
            return Result(h);
        }

        bool ramOk = (long)h.RamEnd == h.RamDest + h.FlashLength;
        checks.Add(new Check("hdr.ram", "Coherencia del fin en RAM", ramOk ? CheckStatus.Ok : CheckStatus.Error,
            ramOk
                ? $"0x{h.RamDest:X8} + 0x{h.FlashLength:X} = 0x{h.RamEnd:X8}"
                : $"Fin RAM 0x{h.RamEnd:X8} ≠ destino 0x{h.RamDest:X8} + longitud 0x{h.FlashLength:X}"));

        if (h.Reserved != 0)
            checks.Add(new Check("hdr.reserved", "Campo +0x10 de la cabecera", CheckStatus.Advertencia,
                $"Vale 0x{h.Reserved:X8}; en todos los binarios analizados es 0."));

        // 3. CRC
        uint crc = Crc32Msb.Compute(fw.AsSpan(MStarLayout.AppFlashStart, (int)(h.FlashEnd - MStarLayout.AppFlashStart)));
        bool crcOk = crc == h.Crc;
        checks.Add(new Check("crc", "CRC-32 de la aplicación", crcOk ? CheckStatus.Ok : CheckStatus.Error,
            crcOk
                ? $"0x{crc:X8} (coincide con la cabecera)"
                : $"Calculado 0x{crc:X8} ≠ cabecera 0x{h.Crc:X8}. El TV rechazaría esta imagen."));

        // 4. Tabla TEL
        var tel = TelTable.TryRead(fw, out string telErr);
        if (tel is null)
        {
            checks.Add(new Check("tel", "Tabla de recursos TEL", CheckStatus.Error, telErr));
            return Result(h, crc);
        }
        checks.Add(new Check("tel", "Tabla de recursos TEL", tel.HasTerminator ? CheckStatus.Ok : CheckStatus.Advertencia,
            $"{tel.Entries.Count} recursos; primero en 0x{tel.FirstResourceOffset:X6}" +
            (tel.HasTerminator ? "" : "; falta el terminador 0xF000")));

        // CRC-16 al final de cada recurso. Solo informativo: el arranque de los firmwares conocidos no lo
        // comprueba (el logo girado por el script Python del Konka lo dejó mal y el TV funciona).
        var badCrc = tel.Entries.Where(e => !Crc16Tel.IsValid(fw.AsSpan((int)e.Offset, (int)e.Length)))
            .Select(e => $"0x{e.Id:X4}").ToList();
        checks.Add(new Check("tel.crc", "CRC-16 de los recursos", badCrc.Count == 0 ? CheckStatus.Ok : CheckStatus.Advertencia,
            badCrc.Count == 0
                ? $"Correcto en los {tel.Entries.Count} recursos"
                : $"No coincide en {string.Join(", ", badCrc)} (recurso modificado sin actualizar su CRC; los firmwares conocidos no lo comprueban al arrancar)"));

        uint limit = tel.FirstResourceOffset;
        if (limit <= MStarLayout.DeflateOffset)
        {
            checks.Add(new Check("tel.limit", "Límite de recursos", CheckStatus.Error,
                $"El primer recurso (0x{limit:X}) está antes del programa comprimido."));
            return Result(h, crc, tel);
        }
        bool fitsOk = h.FlashEnd <= limit;
        checks.Add(new Check("hdr.fit", "La app no pisa los recursos", fitsOk ? CheckStatus.Ok : CheckStatus.Error,
            $"Fin app 0x{h.FlashEnd:X6} / primer recurso 0x{limit:X6}"));

        // 5. Descompresión
        InflateResult inflated;
        try
        {
            inflated = RawInflater.Inflate(fw.AsSpan(MStarLayout.DeflateOffset, (int)(limit - MStarLayout.DeflateOffset)), MStarLayout.MaxAppSize);
        }
        catch (InvalidDataException ex)
        {
            checks.Add(new Check("deflate", "Descompresión del programa", CheckStatus.Error, ex.Message));
            return Result(h, crc, tel);
        }
        int defEnd = MStarLayout.DeflateOffset + inflated.BytesConsumed;
        checks.Add(new Check("deflate", "Descompresión del programa", CheckStatus.Ok,
            $"{Fmt.N(inflated.BytesConsumed)} bytes comprimidos → {Fmt.N(inflated.Data.Length)} bytes"));

        if (defEnd + MStarLayout.TailSize > limit)
        {
            checks.Add(new Check("tail", "Cola tras el deflate", CheckStatus.Error, "La cola de 6 bytes no cabe antes de los recursos."));
            return Result(h, crc, tel, defEnd: defEnd);
        }
        byte[] tail = fw.AsSpan(defEnd, MStarLayout.TailSize).ToArray();
        uint tailSize = BinaryPrimitives.ReadUInt32LittleEndian(tail);
        bool tailOk = tailSize == (uint)inflated.Data.Length;
        checks.Add(new Check("tail.size", "Tamaño en la cola", tailOk ? CheckStatus.Ok : CheckStatus.Error,
            tailOk
                ? $"{Fmt.N(tailSize)} bytes; 2 bytes finales {tail[4]:X2} {tail[5]:X2} (función desconocida, se conservan)"
                : $"Cola dice {Fmt.N(tailSize)} bytes; descomprimido {Fmt.N(inflated.Data.Length)}."));

        bool endOk = h.FlashEnd == (uint)(defEnd + MStarLayout.TailSize);
        checks.Add(new Check("hdr.endmatch", "Fin en flash = fin del deflate + cola", endOk ? CheckStatus.Ok : CheckStatus.Error,
            $"Cabecera 0x{h.FlashEnd:X6} / calculado 0x{defEnd + MStarLayout.TailSize:X6}"));

        // 6. MS_IMG_INFO (solo advertencia: los parches probados no lo actualizan)
        var img = ImgInfo.TryRead(fw);
        if (img is null)
        {
            checks.Add(new Check("imginfo", "Bloque MS_IMG_INFO", CheckStatus.Advertencia, "No se encontró en 0x21180."));
        }
        else
        {
            bool lenOk = img.AppFlashLength == (uint)h.FlashLength;
            string kind = img.IsDeflate ? "deflate (0xDEADBE)" : img.IsLzma ? "LZMA (0xDEADEF)" : $"0x{img.CompressionMagic:X6}";
            checks.Add(new Check("imginfo", "Bloque MS_IMG_INFO", lenOk && img.IsDeflate ? CheckStatus.Ok : CheckStatus.Advertencia,
                $"Compresión {kind}; longitud registrada 0x{img.AppFlashLength:X} " +
                (lenOk ? "(coincide)" : $"≠ actual 0x{h.FlashLength:X} (normal en firmwares ya parcheados)")));
        }

        // 7. Zona libre tras la app: debe estar a cero (así la dejan el compilador y los parches)
        long free = limit - h.FlashEnd;
        bool zeros = fw.AsSpan((int)h.FlashEnd, (int)free).IndexOfAnyExcept((byte)0) < 0;
        checks.Add(new Check("free", "Espacio libre tras la app", zeros ? CheckStatus.Ok : CheckStatus.Advertencia,
            $"{Fmt.N(free)} bytes" + (zeros ? " (a cero)" : " — contiene datos distintos de cero")));

        return Result(h, crc, tel, img, defEnd, tail, inflated.Data, free);
    }

    /// <summary>Busca un texto ASCII imprimible que empiece por <paramref name="prefix"/>.</summary>
    internal static string? FindAscii(ReadOnlySpan<byte> fw, ReadOnlySpan<byte> prefix, int start, int end)
    {
        if (fw.Length < end) return null;
        int i = fw[start..end].IndexOf(prefix);
        if (i < 0) return null;
        i += start;
        int j = i;
        while (j < end && j - i < 64 && fw[j] >= 0x21 && fw[j] < 0x7F) j++;
        return Encoding.ASCII.GetString(fw[i..j]);
    }

    /// <summary>Devuelve el identificador (letras, dígitos, "_") que contiene <paramref name="needle"/> (p. ej. "KK_CUBA_SBOOT_20140718").</summary>
    internal static string? FindTagAround(ReadOnlySpan<byte> fw, ReadOnlySpan<byte> needle, int start, int end)
    {
        if (fw.Length < end) return null;
        int i = fw[start..end].IndexOf(needle);
        if (i < 0) return null;
        i += start;
        int a = i, b = i;
        while (a > start && i - a < 48 && IsTagChar(fw[a - 1])) a--;
        while (b < end && b - i < 64 && IsTagChar(fw[b])) b++;
        return Encoding.ASCII.GetString(fw[a..b]);
    }

    private static bool IsTagChar(byte c) => c is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_';
}

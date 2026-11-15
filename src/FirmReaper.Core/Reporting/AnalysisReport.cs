using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.MStar;

namespace FirmReaper.Core.Reporting;

/// <summary>Genera el informe de un análisis en texto legible y en JSON (mismo contenido).</summary>
public static class AnalysisReport
{
    public static string ToText(FirmwareAnalysis a, Identification id)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FirmReaper — análisis (solo lectura) ===");
        if (a.FilePath.Length > 0) sb.AppendLine($"Archivo:        {a.FilePath}");
        sb.AppendLine($"Tamaño:         {Fmt.N(a.FileSize)} bytes");
        sb.AppendLine($"SHA-256:        {a.Sha256}");
        sb.AppendLine($"Identificación: {id.Message}");
        if (a.BoardId is not null) sb.AppendLine($"ID de placa:    {a.BoardId}");
        if (a.SbootTag is not null) sb.AppendLine($"SBOOT:          {a.SbootTag}");
        sb.AppendLine();

        if (a.Header is { } h)
        {
            sb.AppendLine("Cabecera (0x20000):");
            sb.AppendLine($"  +0x00 inicio flash  0x{h.FlashStart:X8}");
            sb.AppendLine($"  +0x04 destino RAM   0x{h.RamDest:X8}");
            sb.AppendLine($"  +0x08 fin RAM       0x{h.RamEnd:X8}");
            sb.AppendLine($"  +0x0C fin flash     0x{h.FlashEnd:X8}");
            sb.AppendLine($"  +0x10               0x{h.Reserved:X8}");
            sb.AppendLine($"  +0x14 CRC           0x{h.Crc:X8}" + (a.ComputedCrc is { } c ? $"  (calculado 0x{c:X8})" : ""));
            sb.AppendLine();
        }

        if (a.AppSize is { } appSize && a.DeflateEnd is { } de && a.Header is { } hh)
        {
            sb.AppendLine("Programa:");
            sb.AppendLine($"  Comprimido:    0x{MStarLayout.DeflateOffset:X6}–0x{de:X6} ({Fmt.N(de - MStarLayout.DeflateOffset)} bytes)");
            sb.AppendLine($"  Descomprimido: {Fmt.N(appSize)} bytes");
            if (a.Tail is { } t) sb.AppendLine($"  Cola:          {Convert.ToHexString(t)}");
            if (a.FreeBytes is { } f) sb.AppendLine($"  Libre hasta el primer recurso: {Fmt.N(f)} bytes (fin app 0x{hh.FlashEnd:X6})");
            sb.AppendLine();
        }

        if (a.ImgInfo is { } img)
        {
            sb.AppendLine($"MS_IMG_INFO: {img.Texts}");
            sb.AppendLine();
        }

        if (a.Tel is { } tel)
        {
            sb.AppendLine($"Recursos TEL ({tel.Entries.Count}; zona reservada 0x{tel.ReservedSize:X}):");
            sb.AppendLine("  ID     Offset    Longitud  Compresión     Contenido");
            foreach (var e in tel.Entries)
                sb.AppendLine($"  {e.Id:X4}   0x{e.Offset:X6}  0x{e.Length:X6}  {e.Compression,-13}  {e.Content}");
            sb.AppendLine();
        }

        sb.AppendLine("Comprobaciones:");
        foreach (var chk in a.Checks)
        {
            string mark = chk.Status switch { CheckStatus.Ok => "[OK]  ", CheckStatus.Advertencia => "[AVISO]", _ => "[ERROR]" };
            sb.AppendLine($"  {mark} {chk.Title}: {chk.Detail}");
        }
        sb.AppendLine();
        sb.AppendLine(a.ContainerValid ? "RESULTADO: contenedor VÁLIDO." : "RESULTADO: contenedor NO VÁLIDO.");
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        // Acentos legibles en el JSON. El informe nunca se inserta en HTML, así que no hace falta escaparlos.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(FirmwareAnalysis a, Identification id)
    {
        var model = new
        {
            archivo = a.FilePath,
            tamano = a.FileSize,
            sha256 = a.Sha256,
            identificacion = new { tipo = id.Kind, mensaje = id.Message, nombre = id.Image?.Nombre, variante = id.Image?.Variante },
            idPlaca = a.BoardId,
            sboot = a.SbootTag,
            cabecera = a.Header is { } h
                ? new { inicioFlash = Hex(h.FlashStart), destinoRam = Hex(h.RamDest), finRam = Hex(h.RamEnd), finFlash = Hex(h.FlashEnd), reservado = Hex(h.Reserved), crc = Hex(h.Crc) }
                : null,
            crcCalculado = a.ComputedCrc is { } c ? Hex(c) : null,
            finDeflate = a.DeflateEnd is { } d ? Hex((uint)d) : null,
            cola = a.Tail is { } t ? Convert.ToHexString(t) : null,
            appDescomprimida = a.AppSize,
            bytesLibres = a.FreeBytes,
            telZonaReservada = a.Tel is { } tz ? Hex(tz.ReservedSize) : null,
            recursos = a.Tel?.Entries.Select(e => new { id = $"0x{e.Id:X4}", offset = Hex(e.Offset), longitud = Hex(e.Length), compresion = e.Compression, contenido = e.Content }),
            comprobaciones = a.Checks.Select(k => new { k.Id, titulo = k.Title, estado = k.Status, detalle = k.Detail }),
            contenedorValido = a.ContainerValid,
        };
        return JsonSerializer.Serialize(model, JsonOut);
    }

    private static string Hex(uint v) => $"0x{v:X8}";
}

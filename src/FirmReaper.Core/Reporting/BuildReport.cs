using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Reporting;

/// <summary>Informe que acompaña a cada firmware generado (texto legible + JSON con los mismos datos).</summary>
public static class BuildReport
{
    private static readonly string[] Procedure =
    [
        "1. Lee la memoria SPI de la placa 3 veces y comprueba que las 3 huellas coinciden. Guarda el original en 2 sitios.",
        "2. Graba el archivo generado: BORRAR → ESCRIBIR → VERIFICAR. (CH341A negro: comprueba que da 3,3 V en los datos.)",
        "3. Comprueba que la huella SHA-256 del archivo que grabas es la indicada en este informe.",
        "4. Primer arranque: LED de reposo, respuesta al mando, imagen y menús.",
        "5. Si no arranca: vuelve a grabar el original (está en la carpeta respaldo_originales).",
    ];

    public static string ToText(BuildResult r, string inputPath, SavedFiles files, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== FirmReaper — informe de generación ===");
        sb.AppendLine($"Fecha:            {now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Perfil:           {r.Profile.Name}");
        sb.AppendLine($"                  id {r.Profile.Id}, versión {r.Profile.Version}, {(r.Profile.Source == Profiles.ProfileSource.Integrado ? "integrado" : "externo: " + r.Profile.Location)}");
        sb.AppendLine($"                  estado: {(r.Profile.Verified ? "verificado" : "SIN VERIFICAR con el binario real")}; SHA-256 del perfil {r.Profile.FileSha256}");
        sb.AppendLine("Opciones:");
        foreach (var o in r.Profile.Options)
        {
            var v = o.Get(r.Selection[o.Id]);
            sb.AppendLine($"  {o.Label}: {v.Label}");
        }
        sb.AppendLine();
        sb.AppendLine($"Original:         {inputPath}");
        sb.AppendLine($"  SHA-256:        {r.InputSha256}");
        sb.AppendLine($"  Copia:          {files.Backup}{(files.BackupCreated ? "" : " (ya existía)")}");
        sb.AppendLine($"Generado:         {files.Firmware}");
        sb.AppendLine($"  SHA-256:        {r.OutputSha256}");
        if (r.Known is not null)
            sb.AppendLine(r.MatchesKnown
                ? $"  = idéntico a {r.Known.FileName} (versión ya documentada)"
                : $"  ≠ distinto de {r.Known.FileName} (mismo contenido; revisar)");
        sb.AppendLine();
        sb.AppendLine("Cabecera (antes → después):");
        sb.AppendLine($"  fin RAM    0x{r.OldHeader.RamEnd:X8} → 0x{r.NewHeader.RamEnd:X8}");
        sb.AppendLine($"  fin flash  0x{r.OldHeader.FlashEnd:X8} → 0x{r.NewHeader.FlashEnd:X8}");
        sb.AppendLine($"  CRC        0x{r.OldHeader.Crc:X8} → 0x{r.NewHeader.Crc:X8}");
        sb.AppendLine(r.Patches.Count > 0
            ? $"Programa comprimido: {Fmt.N(r.CompressedSize)} bytes ({r.Profile.Compression}); libres {Fmt.N(r.FreeBytes)} bytes"
            : "Programa: sin cambios (no se recomprime)");
        if (r.Logo != Profiles.LogoAction.Ninguna)
            sb.AppendLine($"Logo: {(r.Logo == Profiles.LogoAction.Girar ? "girado 180°" : "propio")}, {Fmt.N(r.LogoLength)} bytes, SHA-256 {r.LogoSha256}");
        sb.AppendLine();
        sb.AppendLine($"Parches ({r.Patches.Count}; {r.ChangedAppBytes} bytes cambiados en la app):");
        foreach (var p in r.Patches)
        {
            sb.AppendLine($"  0x{p.Address:X6}  {p.Original.Length,4} bytes  {p.Description}");
            if (p.Original.Length <= 16)
                sb.AppendLine($"            {Convert.ToHexStringLower(p.Original)} → {Convert.ToHexStringLower(p.New)}");
        }
        if (r.KeyMap is not null && r.Profile.KeyTable is { } kt)
        {
            sb.AppendLine();
            sb.AppendLine($"Tabla del mando ({r.KeyMap.Count} teclas{(r.KeyMapEdited ? ", EDITADA" : ", la del perfil")}):");
            foreach (var k in r.KeyMap)
                sb.AppendLine($"  0x{k.Code:X2} {kt.ButtonName(k.Code),-28} → 0x{k.Index:X2} {kt.Functions.GetValueOrDefault(k.Index)}");
        }
        sb.AppendLine();
        sb.AppendLine("Comprobaciones superadas:");
        foreach (var v in r.Verifications) sb.AppendLine("  [OK] " + v);
        sb.AppendLine();
        sb.AppendLine("Procedimiento de grabación:");
        foreach (var s in Procedure) sb.AppendLine("  " + s);
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string ToJson(BuildResult r, string inputPath, SavedFiles files, DateTimeOffset now)
    {
        var model = new
        {
            fecha = now.ToString("O"),
            perfil = new
            {
                id = r.Profile.Id,
                version = r.Profile.Version,
                nombre = r.Profile.Name,
                origen = r.Profile.Source.ToString(),
                verificado = r.Profile.Verified,
                sha256 = r.Profile.FileSha256,
            },
            opciones = r.Selection,
            original = new { ruta = inputPath, sha256 = r.InputSha256, copia = files.Backup },
            generado = new
            {
                ruta = files.Firmware,
                sha256 = r.OutputSha256,
                versionConocida = r.Known?.FileName,
                identicoAConocida = r.Known is null ? (bool?)null : r.MatchesKnown,
            },
            cabecera = new
            {
                finRam = new[] { $"0x{r.OldHeader.RamEnd:X8}", $"0x{r.NewHeader.RamEnd:X8}" },
                finFlash = new[] { $"0x{r.OldHeader.FlashEnd:X8}", $"0x{r.NewHeader.FlashEnd:X8}" },
                crc = new[] { $"0x{r.OldHeader.Crc:X8}", $"0x{r.NewHeader.Crc:X8}" },
            },
            comprimido = r.CompressedSize,
            logo = r.Logo == Profiles.LogoAction.Ninguna ? null : new { accion = r.Logo.ToString(), longitud = r.LogoLength, sha256 = r.LogoSha256 },
            libres = r.FreeBytes,
            bytesCambiados = r.ChangedAppBytes,
            parches = r.Patches.Select(p => new
            {
                direccion = $"0x{p.Address:X6}",
                longitud = p.Original.Length,
                original = Convert.ToHexStringLower(p.Original),
                nuevo = Convert.ToHexStringLower(p.New),
                descripcion = p.Description,
            }),
            tablaMando = r.KeyMap is null ? null : new
            {
                editada = r.KeyMapEdited,
                teclas = r.KeyMap.Select(k => new { codigo = $"0x{k.Code:X2}", indice = $"0x{k.Index:X2}" }),
            },
            comprobaciones = r.Verifications,
            procedimiento = Procedure,
        };
        return JsonSerializer.Serialize(model, JsonOut);
    }
}

using FirmReaper.Core.Analysis;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;
using FirmReaper.Core.Reporting;

namespace FirmReaper.Cli;

/// <summary>
/// Línea de comandos de FirmReaper.
///   firmreaper info ARCHIVO.BIN [--json]                 análisis completo (solo lectura)
///   firmreaper verificar ARCHIVO.BIN ...                 una línea por archivo
///   firmreaper catalogo                                  imágenes conocidas
///   firmreaper perfiles                                  perfiles disponibles y sus opciones
///   firmreaper generar ARCHIVO.BIN --perfil ID [--opcion clave=valor ...] [--logo LOGO.jpg] --salida CARPETA
/// Códigos de salida: 0 = correcto, 1 = no válido / no reconocido / error, 2 = error de uso.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0)
            return Usage();
        try
        {
            var catalog = FirmwareCatalog.LoadEmbedded();
            return args[0].ToLowerInvariant() switch
            {
                "info" when args.Length >= 2 => Info(args[1], args.Contains("--json"), catalog),
                "verificar" when args.Length >= 2 => Verify(args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)), catalog),
                "catalogo" => ListCatalog(catalog),
                "perfiles" => ListProfiles(LoadRegistry()),
                "generar" when args.Length >= 2 => Generate(args, LoadRegistry()),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or BuildException or ArgumentException)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            FirmReaper — firmware MStar
              firmreaper info ARCHIVO.BIN [--json]
              firmreaper verificar ARCHIVO.BIN [ARCHIVO2.BIN ...]
              firmreaper catalogo
              firmreaper perfiles
              firmreaper generar ARCHIVO.BIN --perfil ID [--opcion clave=valor ...] [--logo LOGO.jpg] --salida CARPETA
            """);
        return 2;
    }

    private static ProfileRegistry LoadRegistry()
    {
        var reg = ProfileRegistry.Load(ProfileRegistry.DefaultExternalFolder());
        foreach (var e in reg.Errors)
            Console.Error.WriteLine("AVISO: perfil externo rechazado: " + e);
        return reg;
    }

    private static int Info(string path, bool json, FirmwareCatalog catalog)
    {
        var a = FirmwareAnalyzer.AnalyzeFile(path);
        var id = Identifier.Identify(a, catalog);
        Console.WriteLine(json ? AnalysisReport.ToJson(a, id) : AnalysisReport.ToText(a, id));
        return a.ContainerValid && id.Kind == IdentificationKind.Conocido ? 0 : 1;
    }

    private static int Verify(IEnumerable<string> paths, FirmwareCatalog catalog)
    {
        int rc = 0;
        foreach (var p in paths)
        {
            var a = FirmwareAnalyzer.AnalyzeFile(p);
            var id = Identifier.Identify(a, catalog);
            string valid = a.ContainerValid ? "CONTENEDOR OK" : "CONTENEDOR NO VÁLIDO";
            Console.WriteLine($"{Path.GetFileName(p)}: {valid} | {id.Message}");
            if (!a.ContainerValid || id.Kind != IdentificationKind.Conocido) rc = 1;
        }
        return rc;
    }

    private static int ListCatalog(FirmwareCatalog catalog)
    {
        foreach (var i in catalog.Images)
            Console.WriteLine($"{i.Sha256[..16]}…  {i.Tipo,-10} {i.Nombre}");
        return 0;
    }

    private static int ListProfiles(ProfileRegistry reg)
    {
        foreach (var p in reg.Profiles)
        {
            Console.WriteLine($"{p.Id}  (v{p.Version}, {(p.Verified ? "verificado" : "SIN VERIFICAR")}, {p.Source.ToString().ToLowerInvariant()})");
            Console.WriteLine($"  {p.Name}");
            Console.WriteLine($"  original: {p.OriginalSha256}");
            foreach (var o in p.Options)
                Console.WriteLine($"  --opcion {o.Id}={string.Join("|", o.Values.Select(v => v.Id))}   ({o.Label}; por defecto {o.Default})");
            if (p.Options.Count == 0)
                Console.WriteLine("  (sin opciones)");
        }
        return 0;
    }

    private static int Generate(string[] args, ProfileRegistry reg)
    {
        string input = args[1];
        string? profileId = null, outDir = null, logoPath = null;
        var selection = new Dictionary<string, string>();
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--perfil" when i + 1 < args.Length: profileId = args[++i]; break;
                case "--salida" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--logo" when i + 1 < args.Length: logoPath = args[++i]; break;
                case "--opcion" when i + 1 < args.Length:
                    var kv = args[++i].Split('=', 2);
                    if (kv.Length != 2) return Usage();
                    selection[kv[0]] = kv[1];
                    break;
                default: return Usage();
            }
        }
        if (profileId is null || outDir is null)
            return Usage();
        var profile = reg.ById(profileId) ?? throw new ArgumentException($"No existe el perfil '{profileId}'.");

        var fw = File.ReadAllBytes(input);
        byte[]? logo = null;
        if (logoPath is not null)
        {
            if (new FileInfo(logoPath).Length > FirmwareBuilder.MaxCustomLogoBytes)
                throw new ArgumentException("El archivo del logo es demasiado grande.");
            logo = File.ReadAllBytes(logoPath);
        }
        var r = FirmwareBuilder.Build(fw, profile, selection, logo, new SyncProgress(Console.Error));
        var saved = OutputWriter.Save(r, input, outDir, DateTimeOffset.Now);
        Console.WriteLine($"Generado: {saved.Firmware}");
        Console.WriteLine($"SHA-256:  {r.OutputSha256}");
        if (r.Known is not null)
            Console.WriteLine(r.MatchesKnown ? $"Idéntico a {r.Known.FileName}." : $"AVISO: distinto de {r.Known.FileName}.");
        Console.WriteLine($"Informe:  {saved.ReportText}");
        Console.WriteLine($"Copia del original: {saved.Backup}");
        return 0;
    }

    /// <summary>Progreso escrito al instante (Progress&lt;T&gt; usaría otro hilo).</summary>
    private sealed class SyncProgress(TextWriter w) : IProgress<string>
    {
        public void Report(string value) => w.WriteLine("  " + value);
    }
}

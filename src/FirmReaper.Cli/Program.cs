using FirmReaper.Core.Analysis;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.Reporting;

namespace FirmReaper.Cli;

/// <summary>
/// Línea de comandos de FirmReaper (etapa 1: solo lectura).
///   firmreaper info ARCHIVO.BIN [--json]   análisis completo
///   firmreaper verificar ARCHIVO.BIN ...   una línea por archivo: identificación + validez
///   firmreaper catalogo                    lista las imágenes conocidas
/// Códigos de salida: 0 = válido/reconocido, 1 = no válido o no reconocido, 2 = error de uso.
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
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            FirmReaper — firmware MStar (etapa 1: solo lectura)
              firmreaper info ARCHIVO.BIN [--json]
              firmreaper verificar ARCHIVO.BIN [ARCHIVO2.BIN ...]
              firmreaper catalogo
            """);
        return 2;
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
}

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FirmReaper.Core.Catalog;

public enum ImageKind
{
    /// <summary>Volcado original del fabricante.</summary>
    Original,
    /// <summary>Versión generada por nosotros a partir de un original.</summary>
    Generado,
    /// <summary>Firmware usado solo como referencia (no se parchea).</summary>
    Referencia,
}

public enum TestState
{
    Pendiente,
    Funciono,
    NoFunciono,
}

/// <summary>Una imagen conocida del catálogo (identificada por su SHA-256).</summary>
public sealed class KnownImage
{
    public required string Sha256 { get; init; }
    public required ImageKind Tipo { get; init; }
    public required string Modelo { get; init; }
    public required string Placa { get; init; }
    public required string Nombre { get; init; }
    /// <summary>SHA-256 del original del que se generó (solo tipo "generado").</summary>
    public string? Origen { get; init; }
    public string? Variante { get; init; }
    public TestState? EstadoPrueba { get; init; }
    public string? Notas { get; init; }
}

internal sealed class CatalogFile
{
    public required int Version { get; init; }
    public required List<KnownImage> Imagenes { get; init; }
}

/// <summary>
/// Catálogo de imágenes conocidas. Se carga desde un recurso incrustado en el ensamblado y se valida
/// de forma estricta: campos desconocidos, huellas mal formadas, duplicados u orígenes inexistentes
/// hacen fallar la carga (un catálogo dañado no debe producir identificaciones erróneas).
/// </summary>
public sealed partial class FirmwareCatalog
{
    public const int SupportedVersion = 1;
    private const int MaxCatalogBytes = 1024 * 1024;

    private readonly Dictionary<string, KnownImage> _bySha;

    public IReadOnlyList<KnownImage> Images { get; }

    private FirmwareCatalog(List<KnownImage> images)
    {
        Images = images;
        _bySha = images.ToDictionary(i => i.Sha256, StringComparer.Ordinal);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        MaxDepth = 8,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        Converters = { new JsonStringEnumConverter(new KebabLowerNamingPolicy(), allowIntegerValues: false) },
    };

    /// <summary>Carga el catálogo incrustado en el programa.</summary>
    public static FirmwareCatalog LoadEmbedded()
    {
        using var s = typeof(FirmwareCatalog).Assembly.GetManifestResourceStream("FirmReaper.Core.catalog.json")
            ?? throw new InvalidOperationException("Falta el catálogo incrustado.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <summary>Analiza y valida un catálogo JSON.</summary>
    /// <exception cref="InvalidDataException">Si el catálogo no es válido.</exception>
    public static FirmwareCatalog Parse(byte[] json)
    {
        if (json.Length > MaxCatalogBytes)
            throw new InvalidDataException("Catálogo demasiado grande.");
        CatalogFile file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json, JsonOptions)
                ?? throw new InvalidDataException("Catálogo vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Catálogo JSON inválido: " + ex.Message, ex);
        }

        if (file.Version != SupportedVersion)
            throw new InvalidDataException($"Versión de catálogo no soportada: {file.Version}.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var img in file.Imagenes)
        {
            if (!Sha256Regex().IsMatch(img.Sha256))
                throw new InvalidDataException($"Huella SHA-256 mal formada: '{img.Sha256}'.");
            if (!seen.Add(img.Sha256))
                throw new InvalidDataException($"Huella repetida en el catálogo: {img.Sha256}.");
            if (string.IsNullOrWhiteSpace(img.Nombre) || string.IsNullOrWhiteSpace(img.Modelo))
                throw new InvalidDataException($"Entrada sin nombre o modelo: {img.Sha256}.");
            bool generated = img.Tipo == ImageKind.Generado;
            if (generated != (img.Origen is not null))
                throw new InvalidDataException($"'origen' solo (y siempre) en imágenes generadas: {img.Sha256}.");
        }
        foreach (var img in file.Imagenes.Where(i => i.Origen is not null))
        {
            var src = file.Imagenes.FirstOrDefault(o => o.Sha256 == img.Origen);
            if (src is null || src.Tipo != ImageKind.Original)
                throw new InvalidDataException($"El origen de {img.Sha256} no es un original del catálogo.");
        }
        return new FirmwareCatalog(file.Imagenes);
    }

    public KnownImage? Find(string sha256) => _bySha.GetValueOrDefault(sha256.ToLowerInvariant());

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Regex();

    /// <summary>"NoFunciono" ⇄ "no-funciono".</summary>
    private sealed class KebabLowerNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => KebabCaseLower.ConvertName(name);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FirmReaper.Core.MStar;

namespace FirmReaper.Core.Profiles;

/// <summary>Un parche ya validado y convertido a bytes.</summary>
public sealed record Patch(uint Address, byte[] Original, byte[] New, string Description)
{
    public uint End => Address + (uint)Original.Length;
}

public sealed record PatchZone(uint From, uint To, string Reason);

public sealed record OptionValue(string Id, string Label, IReadOnlyList<Patch> Patches);

public sealed record ProfileOptionDef(string Id, string Label, string Default, IReadOnlyList<OptionValue> Values)
{
    public OptionValue Get(string valueId) => Values.FirstOrDefault(v => v.Id == valueId)
        ?? throw new ArgumentException($"La opción '{Id}' no tiene el valor '{valueId}'.");
}

public sealed record KnownOutput(IReadOnlyDictionary<string, string> Options, string Sha256, string FileName);

/// <summary>Origen del perfil: integrado en el programa o archivo externo de la carpeta "perfiles".</summary>
public enum ProfileSource
{
    Integrado,
    Externo,
}

/// <summary>Perfil validado, listo para usar.</summary>
public sealed class Profile
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required bool Verified { get; init; }
    public required string OriginalSha256 { get; init; }
    public required uint AppBaseRam { get; init; }
    public required int ZopfliIterations { get; init; }
    public required IReadOnlyList<PatchZone> Zones { get; init; }
    public required IReadOnlyList<Patch> FixedPatches { get; init; }
    public required IReadOnlyList<ProfileOptionDef> Options { get; init; }
    public required IReadOnlyList<KnownOutput> KnownOutputs { get; init; }
    public required ProfileSource Source { get; init; }
    /// <summary>Ruta del archivo (externo) o nombre del recurso (integrado).</summary>
    public required string Location { get; init; }
    /// <summary>SHA-256 del archivo JSON tal cual se cargó (va en el informe).</summary>
    public required string FileSha256 { get; init; }

    public Dictionary<string, string> DefaultSelection() => Options.ToDictionary(o => o.Id, o => o.Default);

    /// <summary>Parches de una selección de opciones (fijos + los del valor elegido en cada opción).</summary>
    public List<Patch> PatchesFor(IReadOnlyDictionary<string, string> selection)
    {
        var unknown = selection.Keys.Where(k => Options.All(o => o.Id != k)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException("Opciones desconocidas: " + string.Join(", ", unknown));
        var list = new List<Patch>(FixedPatches);
        foreach (var o in Options)
            list.AddRange(o.Get(selection.TryGetValue(o.Id, out var v) ? v : o.Default).Patches);
        return list.OrderBy(p => p.Address).ToList();
    }

    public KnownOutput? KnownOutputFor(IReadOnlyDictionary<string, string> selection) =>
        KnownOutputs.FirstOrDefault(k => Options.All(o =>
            k.Options.TryGetValue(o.Id, out var v) &&
            v == (selection.TryGetValue(o.Id, out var s) ? s : o.Default)));
}

/// <summary>
/// Carga y valida perfiles. Un perfil dañado o manipulado se RECHAZA entero; las comprobaciones
/// garantizan que ningún parche pueda escribir fuera de sus zonas permitidas ni solaparse con otro.
/// </summary>
public static partial class ProfileLoader
{
    public const int SupportedFormat = 1;
    private const int MaxProfileBytes = 512 * 1024;
    private const int MaxPatchBytes = 4096;
    private const int MaxPatchesPerProfile = 256;
    private const int MaxZoneBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        MaxDepth = 16,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^0x[0-9A-Fa-f]{1,8}$")]
    private static partial Regex AddrRegex();

    [GeneratedRegex("^([0-9a-f]{2})+$")]
    private static partial Regex HexBytesRegex();

    /// <summary>Perfiles integrados (recursos del ensamblado).</summary>
    public static List<Profile> LoadBuiltin()
    {
        var asm = typeof(ProfileLoader).Assembly;
        var list = new List<Profile>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("FirmReaper.Core.Profiles.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            list.Add(Parse(ms.ToArray(), ProfileSource.Integrado, name));
        }
        return list;
    }

    /// <summary>
    /// Perfiles externos de una carpeta (*.json). Los inválidos se devuelven en <paramref name="errors"/>
    /// y no se cargan. Un externo no puede usar el id de uno integrado.
    /// </summary>
    public static List<Profile> LoadFolder(string folder, IReadOnlyCollection<string> reservedIds, List<string> errors)
    {
        var list = new List<Profile>();
        if (!Directory.Exists(folder)) return list;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxProfileBytes)
                    throw new InvalidDataException("archivo demasiado grande");
                var p = Parse(File.ReadAllBytes(path), ProfileSource.Externo, info.FullName);
                if (reservedIds.Contains(p.Id) || list.Any(x => x.Id == p.Id))
                    throw new InvalidDataException($"el id '{p.Id}' ya existe");
                list.Add(p);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return list;
    }

    /// <summary>Valida y convierte un perfil JSON.</summary>
    /// <exception cref="InvalidDataException">Si el perfil no es válido.</exception>
    public static Profile Parse(byte[] json, ProfileSource source, string location)
    {
        if (json.Length > MaxProfileBytes)
            throw new InvalidDataException("Perfil demasiado grande.");
        ProfileFile f;
        try
        {
            f = JsonSerializer.Deserialize<ProfileFile>(json, JsonOptions) ?? throw new InvalidDataException("Perfil vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("JSON inválido: " + ex.Message, ex);
        }

        void Fail(string msg) => throw new InvalidDataException(msg);

        if (f.Formato != SupportedFormat) Fail($"Formato de perfil no soportado: {f.Formato}.");
        if (!IdRegex().IsMatch(f.Id)) Fail($"Id de perfil inválido: '{f.Id}'.");
        if (f.Version < 1) Fail("La versión debe ser ≥ 1.");
        if (string.IsNullOrWhiteSpace(f.Nombre)) Fail("Falta el nombre.");
        if (f.Estado is not ("verificado" or "sin-verificar")) Fail($"Estado inválido: '{f.Estado}'.");
        if (!Sha256Regex().IsMatch(f.Original.Sha256)) Fail("SHA-256 del original mal formada.");
        if (f.Compresion.Metodo != "zopfli") Fail($"Método de compresión no soportado: '{f.Compresion.Metodo}'.");
        if (f.Compresion.Iteraciones is < 1 or > 1000) Fail("Iteraciones de zopfli fuera de rango (1–1000).");

        uint baseRam = ParseAddr(f.App.BaseRam, "app.baseRam");

        // Zonas
        var zones = new List<PatchZone>();
        foreach (var z in f.ZonasPermitidas)
        {
            uint from = ParseAddr(z.Desde, "zona.desde");
            uint to = ParseAddr(z.Hasta, "zona.hasta");
            if (to <= from) Fail($"Zona vacía o invertida: {z.Desde}–{z.Hasta}.");
            if (from < baseRam) Fail($"Zona {z.Desde} antes del inicio de la app ({f.App.BaseRam}).");
            if (to - from > MaxZoneBytes) Fail($"Zona demasiado grande ({to - from} bytes; máximo {MaxZoneBytes}).");
            if (string.IsNullOrWhiteSpace(z.Motivo)) Fail("Toda zona debe explicar su motivo.");
            zones.Add(new PatchZone(from, to, z.Motivo));
        }
        if (zones.Count == 0) Fail("El perfil no declara zonas permitidas.");
        var sortedZones = zones.OrderBy(z => z.From).ToList();
        for (int i = 1; i < sortedZones.Count; i++)
            if (sortedZones[i].From < sortedZones[i - 1].To) Fail("Zonas permitidas solapadas.");

        int patchCount = 0;
        Patch ToPatch(ProfilePatch p)
        {
            if (++patchCount > MaxPatchesPerProfile) Fail("Demasiados parches en el perfil.");
            uint addr = ParseAddr(p.Direccion, "parche.direccion");
            byte[] orig = ParseHex(p.Original, $"parche {p.Direccion} original");
            byte[] nuevo = ParseHex(p.Nuevo, $"parche {p.Direccion} nuevo");
            if (orig.Length != nuevo.Length) Fail($"Parche {p.Direccion}: original y nuevo de distinta longitud.");
            if (orig.Length > MaxPatchBytes) Fail($"Parche {p.Direccion}: más de {MaxPatchBytes} bytes.");
            if (orig.AsSpan().SequenceEqual(nuevo)) Fail($"Parche {p.Direccion}: no cambia nada.");
            if (string.IsNullOrWhiteSpace(p.Descripcion)) Fail($"Parche {p.Direccion}: falta la descripción.");
            var patch = new Patch(addr, orig, nuevo, p.Descripcion);
            if (!zones.Any(z => patch.Address >= z.From && patch.End <= z.To))
                Fail($"Parche {p.Direccion} (+{orig.Length}) FUERA de las zonas permitidas.");
            return patch;
        }

        var fixedPatches = f.ParchesFijos.Select(ToPatch).ToList();

        var options = new List<ProfileOptionDef>();
        foreach (var o in f.Opciones)
        {
            if (!IdRegex().IsMatch(o.Id)) Fail($"Id de opción inválido: '{o.Id}'.");
            if (options.Any(x => x.Id == o.Id)) Fail($"Opción repetida: '{o.Id}'.");
            if (o.Tipo != "lista") Fail($"Tipo de opción no soportado: '{o.Tipo}'.");
            if (o.Valores.Count < 2) Fail($"La opción '{o.Id}' necesita al menos 2 valores.");
            var values = new List<OptionValue>();
            foreach (var v in o.Valores)
            {
                if (!IdRegex().IsMatch(v.Id)) Fail($"Id de valor inválido: '{v.Id}'.");
                if (values.Any(x => x.Id == v.Id)) Fail($"Valor repetido en '{o.Id}': '{v.Id}'.");
                values.Add(new OptionValue(v.Id, v.Etiqueta, v.Parches.Select(ToPatch).ToList()));
            }
            if (values.All(v => v.Id != o.PorDefecto)) Fail($"El valor por defecto de '{o.Id}' no existe.");
            options.Add(new ProfileOptionDef(o.Id, o.Etiqueta, o.PorDefecto, values));
        }

        // Ninguna combinación de opciones puede producir parches solapados: se comprueba
        // cada parche contra los fijos y contra los de las OTRAS opciones (los valores de una misma
        // opción son excluyentes entre sí).
        var groups = new List<(string Owner, IReadOnlyList<Patch> Patches)> { ("fijos", fixedPatches) };
        foreach (var o in options)
            foreach (var v in o.Values)
                groups.Add(($"{o.Id}={v.Id}", v.Patches));
        for (int a = 0; a < groups.Count; a++)
        {
            CheckNoOverlap(groups[a].Patches, groups[a].Owner, Fail);
            for (int b = a + 1; b < groups.Count; b++)
            {
                string oa = groups[a].Owner.Split('=')[0], ob = groups[b].Owner.Split('=')[0];
                if (oa == ob && oa != "fijos") continue; // misma opción: excluyentes
                foreach (var pa in groups[a].Patches)
                    foreach (var pb in groups[b].Patches)
                        if (pa.Address < pb.End && pb.Address < pa.End)
                            Fail($"Parches solapados: 0x{pa.Address:X} ({groups[a].Owner}) y 0x{pb.Address:X} ({groups[b].Owner}).");
            }
        }

        var known = new List<KnownOutput>();
        foreach (var k in f.SalidasConocidas)
        {
            if (!Sha256Regex().IsMatch(k.Sha256)) Fail("SHA-256 de salida conocida mal formada.");
            if (options.Count != k.Opciones.Count || options.Any(o => !k.Opciones.TryGetValue(o.Id, out var v) || o.Values.All(x => x.Id != v)))
                Fail($"La salida conocida {k.Sha256[..8]}… no define bien todas las opciones.");
            if (k.Archivo.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || k.Archivo.Length > 120)
                Fail($"Nombre de archivo inválido en salida conocida: '{k.Archivo}'.");
            known.Add(new KnownOutput(k.Opciones, k.Sha256, k.Archivo));
        }

        return new Profile
        {
            Id = f.Id,
            Version = f.Version,
            Name = f.Nombre,
            Description = f.Descripcion,
            Verified = f.Estado == "verificado",
            OriginalSha256 = f.Original.Sha256,
            AppBaseRam = baseRam,
            ZopfliIterations = f.Compresion.Iteraciones,
            Zones = zones,
            FixedPatches = fixedPatches,
            Options = options,
            KnownOutputs = known,
            Source = source,
            Location = location,
            FileSha256 = Convert.ToHexStringLower(SHA256.HashData(json)),
        };
    }

    private static void CheckNoOverlap(IReadOnlyList<Patch> patches, string owner, Action<string> fail)
    {
        var s = patches.OrderBy(p => p.Address).ToList();
        for (int i = 1; i < s.Count; i++)
            if (s[i].Address < s[i - 1].End)
                fail($"Parches solapados en {owner}: 0x{s[i - 1].Address:X} y 0x{s[i].Address:X}.");
    }

    private static uint ParseAddr(string s, string what)
    {
        if (!AddrRegex().IsMatch(s))
            throw new InvalidDataException($"{what}: dirección inválida '{s}' (formato 0x…).");
        uint v = uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (v >= 0x8000_0000)
            throw new InvalidDataException($"{what}: dirección fuera de rango '{s}'.");
        return v;
    }

    private static byte[] ParseHex(string s, string what)
    {
        if (!HexBytesRegex().IsMatch(s))
            throw new InvalidDataException($"{what}: hexadecimal inválido (pares de dígitos en minúscula).");
        return Convert.FromHexString(s);
    }

    /// <summary>Comprueba que la base declarada coincide con la del firmware (destino RAM + 0x3800).</summary>
    internal static bool BaseMatches(Profile p, AppHeader h) => p.AppBaseRam == h.RamDest + MStarLayout.UncompressedPrefix;
}

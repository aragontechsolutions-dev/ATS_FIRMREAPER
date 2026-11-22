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

public enum CompressionMethod
{
    Zopfli,
    Zlib,
}

/// <summary>Compresor de la app. <paramref name="Value"/> = iteraciones (Zopfli) o nivel (zlib).</summary>
public sealed record CompressionSpec(CompressionMethod Method, int Value)
{
    public override string ToString() => Method == CompressionMethod.Zopfli
        ? $"Zopfli, {Value} iteraciones"
        : $"zlib 1.3, nivel {Value}";
}

public enum LogoAction
{
    /// <summary>El logo no cambia.</summary>
    Ninguna,
    /// <summary>Girar 180° (re-codificado idéntico a Pillow).</summary>
    Girar,
    /// <summary>Sustituir por una imagen del usuario (JPEG, PNG o BMP; se convierte al formato del original).</summary>
    Propio,
}

/// <summary>
/// Dónde está el logo de arranque y cómo es el original. Dos ubicaciones posibles:
/// - recurso de la tabla TEL (<see cref="TelId"/>), opcionalmente con CRC-16 al final (<see cref="Crc16"/>);
/// - zona fija de la flash [<see cref="FlashFrom"/>, <see cref="FlashTo"/>) rellena con <see cref="Fill"/>.
/// </summary>
public sealed record LogoSpec(int JpegLength, int Width, int Height)
{
    public ushort? TelId { get; init; }
    public bool Crc16 { get; init; }
    public uint FlashFrom { get; init; }
    public uint FlashTo { get; init; }
    public byte Fill { get; init; }

    public static LogoSpec Tel(ushort id, int jpegLength, int width, int height, bool crc16 = false) =>
        new(jpegLength, width, height) { TelId = id, Crc16 = crc16 };

    public static LogoSpec Zone(uint from, uint to, byte fill, int jpegLength, int width, int height) =>
        new(jpegLength, width, height) { FlashFrom = from, FlashTo = to, Fill = fill };

    public string Location => TelId is { } id
        ? $"recurso TEL 0x{id:X4}" + (Crc16 ? " (con CRC-16)" : "")
        : $"zona de flash 0x{FlashFrom:X6}–0x{FlashTo:X6}";
}

public sealed record OptionValue(string Id, string Label, IReadOnlyList<Patch> Patches, LogoAction Logo = LogoAction.Ninguna);

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
    /// <summary>Compresor de la app: Zopfli (iteraciones) o zlib 1.3 (nivel).</summary>
    public required CompressionSpec Compression { get; init; }
    public required IReadOnlyList<PatchZone> Zones { get; init; }
    public required IReadOnlyList<Patch> FixedPatches { get; init; }
    public required IReadOnlyList<ProfileOptionDef> Options { get; init; }
    public required IReadOnlyList<KnownOutput> KnownOutputs { get; init; }
    public LogoSpec? Logo { get; init; }
    /// <summary>Tabla del mando editable (null si el perfil no la tiene).</summary>
    public KeyTableSpec? KeyTable { get; init; }
    /// <summary>Código de cliente del mando editable (null si el perfil no lo tiene).</summary>
    public ClientCodeSpec? ClientCode { get; init; }
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

    /// <summary>Acción sobre el logo para una selección (como mucho una opción actúa sobre el logo).</summary>
    public LogoAction LogoActionFor(IReadOnlyDictionary<string, string> selection)
    {
        foreach (var o in Options)
        {
            var v = o.Get(selection.TryGetValue(o.Id, out var s) ? s : o.Default);
            if (v.Logo != LogoAction.Ninguna) return v.Logo;
        }
        return LogoAction.Ninguna;
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
        CompressionSpec compression = null!;
        switch (f.Compresion.Metodo)
        {
            case "zopfli":
                if (f.Compresion.Iteraciones is not (>= 1 and <= 1000) || f.Compresion.Nivel is not null)
                    Fail("zopfli necesita 'iteraciones' (1–1000) y no admite 'nivel'.");
                compression = new CompressionSpec(CompressionMethod.Zopfli, f.Compresion.Iteraciones!.Value);
                break;
            case "zlib":
                if (f.Compresion.Nivel is not (>= 4 and <= 9) || f.Compresion.Iteraciones is not null)
                    Fail("zlib necesita 'nivel' (4–9) y no admite 'iteraciones'.");
                compression = new CompressionSpec(CompressionMethod.Zlib, f.Compresion.Nivel!.Value);
                break;
            default:
                Fail($"Método de compresión no soportado: '{f.Compresion.Metodo}'.");
                break;
        }

        uint baseRam = ParseAddr(f.App.BaseRam, "app.baseRam");

        // Zonas
        var zones = new List<PatchZone>();
        foreach (var z in f.ZonasPermitidas)
        {
            uint from = ParseAddr(z.Desde, "zona.desde");
            uint to = ParseAddr(z.Hasta, "zona.hasta");
            if (to <= from) Fail($"Zona vacía o invertida: {z.Desde}–{z.Hasta}.");
            // Antes de la app comprimida solo se admite el código de arranque sin comprimir (flash 0x23080–0x23880),
            // y sin cruzar al principio de la app: nunca MS_IMG_INFO ni la tabla TEL.
            uint stubFrom = baseRam - (uint)(MStarLayout.DeflateOffset - MStarLayout.StubCodeOffset);
            if (from < baseRam && (from < stubFrom || to > baseRam))
                Fail($"Zona {z.Desde} antes del inicio de la app ({f.App.BaseRam}) y fuera del código de arranque sin comprimir.");
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
                var logo = v.Logo switch
                {
                    null => LogoAction.Ninguna,
                    "girar" => LogoAction.Girar,
                    "propio" => LogoAction.Propio,
                    _ => throw new InvalidDataException($"Acción de logo desconocida en '{o.Id}={v.Id}': '{v.Logo}'."),
                };
                values.Add(new OptionValue(v.Id, v.Etiqueta, v.Parches.Select(ToPatch).ToList(), logo));
            }
            if (values.All(v => v.Id != o.PorDefecto)) Fail($"El valor por defecto de '{o.Id}' no existe.");
            options.Add(new ProfileOptionDef(o.Id, o.Etiqueta, o.PorDefecto, values));
        }

        // Logo: si algún valor actúa sobre él, el perfil debe describir el recurso, y solo una opción
        // puede hacerlo (dos acciones sobre el mismo recurso serían contradictorias).
        LogoSpec? logoSpec = null;
        if (f.Logo is { } lg)
        {
            if (lg.LongitudJpeg is < 4 or > 4 * 1024 * 1024) Fail("logo.longitudJpeg fuera de rango.");
            if (lg.Ancho is < 1 or > 4096 || lg.Alto is < 1 or > 4096) Fail("Dimensiones del logo fuera de rango.");
            if ((lg.IdTel is null) == (lg.ZonaFlash is null)) Fail("logo: indique 'idTel' o 'zonaFlash' (uno y solo uno).");
            if (lg.IdTel is not null)
            {
                uint id = ParseAddr(lg.IdTel, "logo.idTel");
                if (id > 0xFFFF || id == TelTable.TerminatorId) Fail("logo.idTel fuera de rango.");
                logoSpec = LogoSpec.Tel((ushort)id, lg.LongitudJpeg, lg.Ancho, lg.Alto, lg.Crc16);
            }
            else
            {
                if (lg.Crc16) Fail("logo.crc16 solo se aplica a un recurso TEL.");
                var z = lg.ZonaFlash!;
                uint from = ParseAddr(z.Desde, "logo.zonaFlash.desde");
                uint to = ParseAddr(z.Hasta, "logo.zonaFlash.hasta");
                uint fill = ParseAddr(z.Relleno, "logo.zonaFlash.relleno");
                if (fill > 0xFF) Fail("logo.zonaFlash.relleno debe ser un byte.");
                // La zona nunca puede tocar SBOOT/PM, la cabecera ni la tabla TEL; el resto se comprueba al generar.
                if (from < MStarLayout.TelOffset + 0x1000 || to <= from || to > MStarLayout.FlashSize)
                    Fail("logo.zonaFlash fuera de rango.");
                if (lg.LongitudJpeg > to - from) Fail("logo.longitudJpeg no cabe en la zona.");
                logoSpec = LogoSpec.Zone(from, to, (byte)fill, lg.LongitudJpeg, lg.Ancho, lg.Alto);
            }
        }
        var logoOptions = options.Where(o => o.Values.Any(v => v.Logo != LogoAction.Ninguna)).ToList();
        if (logoOptions.Count > 0 && logoSpec is null) Fail("Hay acciones sobre el logo pero el perfil no define 'logo'.");
        if (logoOptions.Count > 1) Fail("Solo una opción puede actuar sobre el logo.");

        bool InZones(uint addr, int len) => zones.Any(z => addr >= z.From && addr + (uint)len <= z.To);
        IEnumerable<(string Owner, Patch P)> AllPatches() =>
            fixedPatches.Select(p => ("fijos", p)).Concat(options.SelectMany(o => o.Values.SelectMany(v => v.Patches.Select(p => ($"{o.Id}={v.Id}", p)))));

        // Tabla del mando editable. Ligada a un parche: debe ser EXACTAMENTE (dirección y longitud) un parche de la
        // opción indicada. En su sitio: los bytes originales van en el perfil, debe estar dentro de una zona
        // permitida y ningún parche puede tocarla. Así el editor nunca escribe fuera de lo que el perfil autoriza.
        KeyTableSpec? keyTable = null;
        if (f.TablaMando is { } tm)
        {
            var format = tm.Formato switch
            {
                null or "lista8" => KeyTableFormat.Lista8,
                "lista4" => KeyTableFormat.Lista4,
                "directa256" => KeyTableFormat.Directa256,
                _ => throw new InvalidDataException($"tablaMando.formato desconocido: '{tm.Formato}'."),
            };
            uint addr = ParseAddr(tm.Direccion, "tablaMando.direccion");
            int entries = 256;
            uint prefix = 0;
            if (format == KeyTableFormat.Directa256)
            {
                if (tm.Entradas is not null || tm.PrefijoCodigo is not null) Fail("tablaMando directa256 no admite 'entradas' ni 'prefijoCodigo'.");
            }
            else
            {
                if (tm.Entradas is not (>= 2 and <= 512)) Fail("tablaMando.entradas fuera de rango (2–512).");
                entries = tm.Entradas!.Value;
                if (format == KeyTableFormat.Lista8)
                {
                    prefix = ParseAddr(tm.PrefijoCodigo ?? throw new InvalidDataException("tablaMando lista8 necesita 'prefijoCodigo'."), "tablaMando.prefijoCodigo");
                    if ((prefix & 0xFF) != 0) Fail("tablaMando.prefijoCodigo debe tener a 0 el byte bajo.");
                }
                else if (tm.PrefijoCodigo is not null)
                {
                    Fail("tablaMando lista4 no admite 'prefijoCodigo'.");
                }
            }
            var functions = new Dictionary<byte, string>();
            foreach (var fn in tm.Funciones)
            {
                byte idx = KeyTableSpec.ParseByte(fn.Indice);
                if (idx == 0xFF) Fail("tablaMando: la función 0xFF está reservada (sin función / terminador).");
                if (string.IsNullOrWhiteSpace(fn.Nombre)) Fail($"tablaMando: función 0x{idx:X2} sin nombre.");
                if (!functions.TryAdd(idx, fn.Nombre)) Fail($"tablaMando: función 0x{idx:X2} repetida.");
            }
            var buttons = new List<(byte, string)>();
            foreach (var bt in tm.Botones)
            {
                byte code = KeyTableSpec.ParseByte(bt.Codigo);
                if (string.IsNullOrWhiteSpace(bt.Nombre)) Fail($"tablaMando: botón 0x{code:X2} sin nombre.");
                if (buttons.Any(x => x.Item1 == code)) Fail($"tablaMando: botón 0x{code:X2} repetido.");
                buttons.Add((code, bt.Nombre));
            }
            var probe = new KeyTableSpec
            {
                Format = format, Address = addr, Entries = entries, CodePrefix = prefix,
                Functions = functions, Buttons = buttons, Default = [],
            };
            byte[] tableBytes;
            byte[]? original = null;
            if ((tm.Opcion is null) != (tm.Valor is null)) Fail("tablaMando: 'opcion' y 'valor' van juntos.");
            if (tm.Opcion is not null)
            {
                if (tm.Original is not null) Fail("tablaMando ligada a un parche no admite 'original'.");
                var opt = options.FirstOrDefault(o => o.Id == tm.Opcion) ?? throw new InvalidDataException($"tablaMando: no existe la opción '{tm.Opcion}'.");
                var val = opt.Values.FirstOrDefault(v => v.Id == tm.Valor) ?? throw new InvalidDataException($"tablaMando: la opción '{tm.Opcion}' no tiene el valor '{tm.Valor}'.");
                var tablePatch = val.Patches.FirstOrDefault(p => p.Address == addr);
                if (tablePatch is null || tablePatch.New.Length != probe.ByteLength)
                    Fail("tablaMando: no hay un parche con esa dirección y longitud en la opción indicada.");
                tableBytes = tablePatch!.New;
            }
            else
            {
                original = ParseHex(tm.Original ?? throw new InvalidDataException("tablaMando en su sitio necesita 'original' (o 'opcion'/'valor')."), "tablaMando.original");
                if (original.Length != probe.ByteLength) Fail($"tablaMando.original debe tener {probe.ByteLength} bytes.");
                if (!InZones(addr, original.Length)) Fail("tablaMando: la tabla no está dentro de una zona permitida.");
                foreach (var (owner, p) in AllPatches())
                    if (p.Address < addr + (uint)original.Length && addr < p.End)
                        Fail($"tablaMando: la tabla se solapa con el parche 0x{p.Address:X} ({owner}).");
                tableBytes = original;
            }
            List<KeyAssignment> def;
            try
            {
                def = probe.Decode(tableBytes);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("tablaMando: la tabla por defecto no es válida: " + ex.Message);
            }
            var errs = probe.Validate(def);
            if (errs.Count > 0) Fail("tablaMando: la tabla por defecto no es válida: " + string.Join(" ", errs));
            if (!probe.Encode(def).AsSpan().SequenceEqual(tableBytes))
                Fail("tablaMando: la tabla por defecto no se puede reconstruir exactamente (bytes de relleno inesperados).");
            if (tm.Nota is { Length: > 1000 }) Fail("tablaMando.nota demasiado larga.");
            keyTable = new KeyTableSpec
            {
                Format = format, Address = addr, Entries = entries, CodePrefix = prefix,
                OptionId = tm.Opcion, ValueId = tm.Valor, OriginalBytes = original,
                Functions = functions, Buttons = buttons, Default = def, Note = tm.Nota,
            };
        }

        // Código de cliente del mando: dentro de una zona permitida; los parches que lo toquen deben ser exactamente
        // ese campo (misma dirección y longitud) y partir de los bytes originales declarados.
        ClientCodeSpec? clientCode = null;
        if (f.ClienteMando is { } cm)
        {
            var cf = cm.Formato switch
            {
                "bytes-invertidos" => ClientCodeFormat.BytesInvertidos,
                "aeon-sfeqi" => ClientCodeFormat.AeonSfeqi,
                _ => throw new InvalidDataException($"clienteMando.formato desconocido: '{cm.Formato}'."),
            };
            if (cf == ClientCodeFormat.AeonSfeqi ? cm.Registro is not (>= 0 and <= 31) : cm.Registro is not null)
                Fail("clienteMando.registro: obligatorio (0–31) solo con aeon-sfeqi.");
            if (!NecCustomer.TryParse(cm.Original, out var orig)) Fail("clienteMando.original inválido (formato \"00 BF\").");
            var spec = new ClientCodeSpec
            {
                Address = ParseAddr(cm.Direccion, "clienteMando.direccion"),
                Format = cf,
                Register = cm.Registro ?? 0,
                Original = orig,
            };
            if (spec.WhyNot(orig) is { } why) Fail("clienteMando.original: " + why);
            // Campos de reposo (copias del código que el chip usa para encender desde reposo).
            var standby = new List<ClientStandbyField>();
            foreach (var r in cm.Reposo ?? [])
            {
                if (r.Byte is not (1 or 2)) Fail("clienteMando.reposo.byte debe ser 1 o 2.");
                if (r.Registro is < 0 or > 31) Fail("clienteMando.reposo.registro fuera de rango (0–31).");
                standby.Add(new ClientStandbyField(ParseAddr(r.Direccion, "clienteMando.reposo.direccion"), r.Byte, r.Registro));
            }
            if (standby.Count > 8 || standby.Select(x => x.Address).Distinct().Count() != standby.Count)
                Fail("clienteMando.reposo: como mucho 8 campos y sin direcciones repetidas.");
            spec = new ClientCodeSpec
            {
                Address = spec.Address, Format = spec.Format, Register = spec.Register, Original = orig, Standby = standby,
            };

            // Todos los campos (principal + reposo): en zona, sin solaparse entre sí ni con la tabla.
            var fields = new List<(uint Addr, int Len, string Name)> { (spec.Address, spec.ByteLength, "principal") };
            fields.AddRange(standby.Select(x => (x.Address, ClientCodeSpec.StandbyFieldLength, $"reposo (byte {x.Which})")));
            foreach (var (fa, fl, fn) in fields)
            {
                if (!InZones(fa, fl)) Fail($"clienteMando: el campo {fn} no está dentro de una zona permitida.");
                if (keyTable is not null && fa < keyTable.Address + (uint)keyTable.ByteLength && keyTable.Address < fa + (uint)fl)
                    Fail($"clienteMando: el campo {fn} se solapa con la tabla del mando.");
                foreach (var (ga, gl, gn) in fields)
                    if (gn != fn && fa < ga + (uint)gl && ga < fa + (uint)fl) Fail($"clienteMando: los campos {fn} y {gn} se solapan.");
            }

            // Parches de las opciones que tocan algún campo: deben ser exactamente ese campo, partir del original y,
            // dentro de un mismo valor de opción, cambiar TODOS los campos al MISMO código (si no, el TV obedecería
            // a un mando encendido y a otro en reposo).
            byte[] OrigOf(int k) => k == 0 ? spec.OriginalBytes : spec.EncodeStandby(standby[k - 1], orig);
            var byOwner = new Dictionary<string, Dictionary<int, byte[]>>();
            foreach (var (owner, p) in AllPatches())
            {
                for (int k = 0; k < fields.Count; k++)
                {
                    var (fa, fl, fn) = fields[k];
                    if (p.Address >= fa + (uint)fl || fa >= p.End) continue;
                    if (p.Address != fa || p.Original.Length != fl)
                        Fail($"clienteMando: el parche 0x{p.Address:X} ({owner}) toca el campo {fn} del código de cliente sin ser exactamente ese campo.");
                    if (!p.Original.AsSpan().SequenceEqual(OrigOf(k)))
                        Fail($"clienteMando: el parche 0x{p.Address:X} ({owner}) no parte del código original declarado.");
                    if (!byOwner.TryGetValue(owner, out var m)) byOwner[owner] = m = [];
                    m[k] = p.New;
                }
            }
            foreach (var (owner, m) in byOwner)
            {
                if (m.Count != fields.Count)
                    Fail($"clienteMando: la opción {owner} cambia solo una parte del código de cliente (faltan campos de reposo o el principal).");
                var main = spec.Decode(m[0]) ?? throw new InvalidDataException($"clienteMando: el parche de {owner} no contiene un código de cliente válido.");
                for (int k = 1; k < fields.Count; k++)
                    if (!m[k].AsSpan().SequenceEqual(spec.EncodeStandby(standby[k - 1], main)))
                        Fail($"clienteMando: en {owner} el campo de reposo no coincide con el código principal ({main}).");
            }
            clientCode = spec;
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
            Compression = compression,
            Zones = zones,
            FixedPatches = fixedPatches,
            Options = options,
            KnownOutputs = known,
            Logo = logoSpec,
            KeyTable = keyTable,
            ClientCode = clientCode,
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

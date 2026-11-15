using System.Buffers.Binary;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace FirmReaper.Core.Profiles;

/// <summary>Una asignación botón (código IR) → función (índice del firmware).</summary>
public sealed record KeyAssignment(byte Code, byte Index);

/// <summary>
/// Tabla código → función de un mando, tal como la guarda el firmware (formato LG TP.V56):
/// N entradas de 8 bytes: u32 LE (prefijo | código), u8 índice, 3 bytes a 0; las sobrantes son el
/// terminador u32 0x0000FFFF, índice 0xFF. El firmware busca por código y se queda con la PRIMERA coincidencia.
/// </summary>
public sealed class KeyTableSpec
{
    public const int EntrySize = 8;

    public required uint Address { get; init; }
    public required int Entries { get; init; }
    public required uint CodePrefix { get; init; }
    public required string OptionId { get; init; }
    public required string ValueId { get; init; }
    /// <summary>Funciones permitidas: índice → nombre.</summary>
    public required IReadOnlyDictionary<byte, string> Functions { get; init; }
    /// <summary>Botones del mando (en orden): código → rótulo.</summary>
    public required IReadOnlyList<(byte Code, string Name)> Buttons { get; init; }
    /// <summary>Asignación por defecto (la del perfil, p. ej. la v5 probada).</summary>
    public required IReadOnlyList<KeyAssignment> Default { get; init; }

    public int ByteLength => Entries * EntrySize;

    /// <summary>Máximo de botones con función (una entrada queda para el terminador).</summary>
    public int MaxAssignments => Entries - 1;

    public string ButtonName(byte code) => Buttons.FirstOrDefault(b => b.Code == code).Name ?? $"0x{code:X2}";

    /// <summary>Comprueba una asignación; devuelve la lista de errores (vacía si es válida).</summary>
    public List<string> Validate(IReadOnlyList<KeyAssignment> map)
    {
        var errors = new List<string>();
        if (map.Count == 0) errors.Add("La tabla no tiene ninguna tecla.");
        if (map.Count > MaxAssignments) errors.Add($"Demasiadas teclas: {map.Count} (máximo {MaxAssignments}).");
        foreach (var dup in map.GroupBy(a => a.Code).Where(g => g.Count() > 1))
            errors.Add($"El botón {ButtonName(dup.Key)} (0x{dup.Key:X2}) aparece {dup.Count()} veces.");
        foreach (var a in map)
        {
            if (Buttons.All(b => b.Code != a.Code)) errors.Add($"Código 0x{a.Code:X2}: no es un botón de este mando.");
            if (!Functions.ContainsKey(a.Index)) errors.Add($"Botón {ButtonName(a.Code)}: la función 0x{a.Index:X2} no está permitida.");
            if ((CodePrefix | a.Code) == 0xFFFF) errors.Add($"Código 0x{a.Code:X2}: coincide con el terminador.");
        }
        return errors;
    }

    /// <summary>Bytes de la tabla para el firmware.</summary>
    public byte[] Encode(IReadOnlyList<KeyAssignment> map)
    {
        var errors = Validate(map);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var t = new byte[ByteLength];
        for (int i = 0; i < Entries; i++)
        {
            var e = t.AsSpan(i * EntrySize, EntrySize);
            if (i < map.Count)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(e, CodePrefix | map[i].Code);
                e[4] = map[i].Index;
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(e, 0xFFFF);
                e[4] = 0xFF;
            }
        }
        return t;
    }

    /// <summary>Lee una tabla en formato de firmware (hasta el primer terminador).</summary>
    public List<KeyAssignment> Decode(ReadOnlySpan<byte> table)
    {
        if (table.Length != ByteLength) throw new InvalidDataException("Longitud de tabla incorrecta.");
        var list = new List<KeyAssignment>();
        for (int i = 0; i < Entries; i++)
        {
            var e = table.Slice(i * EntrySize, EntrySize);
            uint code = BinaryPrimitives.ReadUInt32LittleEndian(e);
            if (code == 0xFFFF) break;
            if ((code & ~0xFFu) != CodePrefix || e[5] != 0 || e[6] != 0 || e[7] != 0)
                throw new InvalidDataException($"Entrada {i} de la tabla con formato inesperado.");
            list.Add(new KeyAssignment((byte)code, e[4]));
        }
        return list;
    }

    public bool IsDefault(IReadOnlyList<KeyAssignment> map) => map.SequenceEqual(Default);

    // ------------------------------------------------------------ archivo JSON de tabla (importar/exportar)

    private sealed class KeyMapFile
    {
        public required int Formato { get; init; }
        public required string Perfil { get; init; }
        public required List<KeyMapEntry> Asignaciones { get; init; }
    }

    private sealed class KeyMapEntry
    {
        public required string Codigo { get; init; }
        public required string Indice { get; init; }
        /// <summary>Informativos (se ignoran al importar).</summary>
        public string? Boton { get; init; }
        public string? Funcion { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        MaxDepth = 8,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public string ToJson(string profileId, IReadOnlyList<KeyAssignment> map) =>
        JsonSerializer.Serialize(new KeyMapFile
        {
            Formato = 1,
            Perfil = profileId,
            Asignaciones = map.Select(a => new KeyMapEntry
            {
                Codigo = $"0x{a.Code:X2}",
                Indice = $"0x{a.Index:X2}",
                Boton = ButtonName(a.Code),
                Funcion = Functions.GetValueOrDefault(a.Index),
            }).ToList(),
        }, JsonOpts);

    /// <summary>Lee y valida un archivo de tabla.</summary>
    /// <exception cref="InvalidDataException">Si no es válido para este perfil.</exception>
    public List<KeyAssignment> FromJson(byte[] json, string profileId)
    {
        if (json.Length > 256 * 1024) throw new InvalidDataException("Archivo de tabla demasiado grande.");
        KeyMapFile f;
        try
        {
            f = JsonSerializer.Deserialize<KeyMapFile>(json, JsonOpts) ?? throw new InvalidDataException("Archivo vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Archivo de tabla no válido: " + ex.Message, ex);
        }
        if (f.Formato != 1) throw new InvalidDataException($"Formato de tabla no soportado: {f.Formato}.");
        if (f.Perfil != profileId) throw new InvalidDataException($"La tabla es del perfil '{f.Perfil}', no de '{profileId}'.");
        var map = f.Asignaciones.Select(e => new KeyAssignment(ParseByte(e.Codigo), ParseByte(e.Indice))).ToList();
        var errors = Validate(map);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        return map;
    }

    internal static byte ParseByte(string s)
    {
        if (s.Length is < 3 or > 4 || !s.StartsWith("0x", StringComparison.Ordinal) ||
            !byte.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte v))
            throw new InvalidDataException($"Valor inválido '{s}' (se espera 0x00–0xFF).");
        return v;
    }
}

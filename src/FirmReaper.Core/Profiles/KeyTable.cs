using System.Buffers.Binary;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace FirmReaper.Core.Profiles;

/// <summary>Una asignación botón (código IR del comando) → función (valor que usa el firmware).</summary>
public sealed record KeyAssignment(byte Code, byte Index);

/// <summary>Cómo guarda cada firmware su tabla código → función.</summary>
public enum KeyTableFormat
{
    /// <summary>
    /// LG TP.V56: N entradas de 8 bytes: u32 LE (prefijo | código), u8 función, 3 bytes a 0. Las sobrantes son
    /// el terminador u32 0x0000FFFF, función 0xFF. El firmware se queda con la PRIMERA coincidencia.
    /// </summary>
    Lista8,

    /// <summary>
    /// Konka TP.VST59S: N entradas de 4 bytes: código, función, 0, 0. Termina en la primera entrada con función
    /// 0xFF (las sobrantes: FF FF 00 00). Al arrancar se copia a una tabla en RAM indexada por código.
    /// </summary>
    Lista4,

    /// <summary>ATEC/Soyea: 256 bytes; el byte número "código" es la función (0xFF = el botón no hace nada).</summary>
    Directa256,
}

/// <summary>
/// Tabla código → función de un mando, en el formato de su firmware. Dos modos:
/// - ligada a un parche: la tabla es el parche de una opción (LG: "Mando Soyea = sí");
/// - en su sitio: se edita la tabla original del firmware (ATEC, Konka); <see cref="OriginalBytes"/> son sus
///   bytes en el original y, si se edita, se genera un parche nuevo sobre ellos.
/// </summary>
public sealed class KeyTableSpec
{
    public const int EntrySize = 8;

    public required KeyTableFormat Format { get; init; }
    public required uint Address { get; init; }
    /// <summary>Entradas de la tabla (Lista8/Lista4, terminador incluido). 256 en Directa256.</summary>
    public required int Entries { get; init; }
    /// <summary>Solo Lista8: valor que se suma al código en cada entrada (u32), p. ej. 0x0100.</summary>
    public uint CodePrefix { get; init; }
    /// <summary>Opción/valor cuyo parche ES la tabla (null = tabla en su sitio, siempre editable).</summary>
    public string? OptionId { get; init; }
    public string? ValueId { get; init; }
    /// <summary>Tabla en su sitio: bytes en el firmware original.</summary>
    public byte[]? OriginalBytes { get; init; }
    /// <summary>Funciones permitidas: valor → nombre.</summary>
    public required IReadOnlyDictionary<byte, string> Functions { get; init; }
    /// <summary>Botones conocidos (en orden): código → rótulo. Se admiten además otros códigos (otros mandos).</summary>
    public required IReadOnlyList<(byte Code, string Name)> Buttons { get; init; }
    /// <summary>Asignación por defecto (la del parche del perfil o la del firmware original).</summary>
    public required IReadOnlyList<KeyAssignment> Default { get; init; }
    /// <summary>Nota para el usuario (p. ej. cómo funciona la cruceta en ese TV).</summary>
    public string? Note { get; init; }

    public bool InPlace => OptionId is null;

    public int ByteLength => Format switch
    {
        KeyTableFormat.Lista8 => Entries * EntrySize,
        KeyTableFormat.Lista4 => Entries * 4,
        _ => 256,
    };

    /// <summary>Máximo de botones con función (en las listas, una entrada queda para el terminador).</summary>
    public int MaxAssignments => Format == KeyTableFormat.Directa256 ? 256 : Entries - 1;

    public string ButtonName(byte code) =>
        Buttons.FirstOrDefault(b => b.Code == code).Name ?? $"código 0x{code:X2} (botón de otro mando)";

    /// <summary>¿Se aplica la tabla con esta selección? (en su sitio: siempre).</summary>
    public bool AppliesTo(IReadOnlyDictionary<string, string> selection, Profile profile) =>
        InPlace || (selection.TryGetValue(OptionId!, out var v) ? v : profile.Options.First(o => o.Id == OptionId).Default) == ValueId;

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
            if (!Functions.ContainsKey(a.Index)) errors.Add($"Botón {ButtonName(a.Code)}: la función 0x{a.Index:X2} no está permitida.");
            if (Format == KeyTableFormat.Lista8 && (CodePrefix | a.Code) == 0xFFFF)
                errors.Add($"Código 0x{a.Code:X2}: coincide con el terminador de la tabla.");
        }
        return errors;
    }

    /// <summary>Bytes de la tabla para el firmware.</summary>
    public byte[] Encode(IReadOnlyList<KeyAssignment> map)
    {
        var errors = Validate(map);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var t = new byte[ByteLength];
        switch (Format)
        {
            case KeyTableFormat.Lista8:
                for (int i = 0; i < Entries; i++)
                {
                    var e = t.AsSpan(i * EntrySize, EntrySize);
                    BinaryPrimitives.WriteUInt32LittleEndian(e, i < map.Count ? CodePrefix | map[i].Code : 0xFFFF);
                    e[4] = i < map.Count ? map[i].Index : (byte)0xFF;
                }
                break;
            case KeyTableFormat.Lista4:
                for (int i = 0; i < Entries; i++)
                {
                    t[i * 4] = i < map.Count ? map[i].Code : (byte)0xFF;
                    t[i * 4 + 1] = i < map.Count ? map[i].Index : (byte)0xFF;
                }
                break;
            default:
                Array.Fill(t, (byte)0xFF);
                foreach (var a in map) t[a.Code] = a.Index;
                break;
        }
        return t;
    }

    /// <summary>Lee una tabla en formato de firmware.</summary>
    public List<KeyAssignment> Decode(ReadOnlySpan<byte> table)
    {
        if (table.Length != ByteLength) throw new InvalidDataException("Longitud de tabla incorrecta.");
        var list = new List<KeyAssignment>();
        switch (Format)
        {
            case KeyTableFormat.Lista8:
                for (int i = 0; i < Entries; i++)
                {
                    var e = table.Slice(i * EntrySize, EntrySize);
                    uint code = BinaryPrimitives.ReadUInt32LittleEndian(e);
                    if (code == 0xFFFF) break;
                    if ((code & ~0xFFu) != CodePrefix || e[5] != 0 || e[6] != 0 || e[7] != 0)
                        throw new InvalidDataException($"Entrada {i} de la tabla con formato inesperado.");
                    list.Add(new KeyAssignment((byte)code, e[4]));
                }
                break;
            case KeyTableFormat.Lista4:
                int n = -1;
                for (int i = 0; i < Entries; i++)
                {
                    var e = table.Slice(i * 4, 4);
                    if (e[2] != 0 || e[3] != 0) throw new InvalidDataException($"Entrada {i} de la tabla con formato inesperado.");
                    if (e[1] == 0xFF)
                    {
                        n = i;
                        break;
                    }
                    list.Add(new KeyAssignment(e[0], e[1]));
                }
                if (n < 0) throw new InvalidDataException("La tabla no tiene terminador (función 0xFF).");
                break;
            default:
                for (int c = 0; c < 256; c++)
                    if (table[c] != 0xFF) list.Add(new KeyAssignment((byte)c, table[c]));
                break;
        }
        return list;
    }

    /// <summary>True si la asignación produce exactamente los mismos bytes que la del perfil.</summary>
    public bool IsDefault(IReadOnlyList<KeyAssignment> map) =>
        Validate(map).Count == 0 && Encode(map).AsSpan().SequenceEqual(Encode(Default));

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
        if (f.Asignaciones.Count > 256) throw new InvalidDataException("Demasiadas asignaciones.");
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

/// <summary>
/// Código de cliente de un mando NEC: los dos bytes que el mando envía antes del comando, EN EL ORDEN EN QUE LOS
/// ENVÍA (p. ej. el Soyea LKK32CMB = 37 78). Cada firmware lo guarda a su manera (ver <see cref="ClientCodeFormat"/>).
/// </summary>
public readonly record struct NecCustomer(byte B1, byte B2)
{
    public override string ToString() => $"{B1:X2} {B2:X2}";

    /// <summary>Acepta "37 78", "3778" o "0x3778" (siempre en el orden de envío).</summary>
    public static bool TryParse(string? s, out NecCustomer value)
    {
        value = default;
        if (s is null) return false;
        var t = s.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.Length != 4 || !ushort.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort v))
            return false;
        value = new NecCustomer((byte)(v >> 8), (byte)v);
        return true;
    }
}

/// <summary>
/// Cómo guarda cada firmware el código de cliente (comprobado con el mando Soyea "37 78": el LG lo tiene como
/// 78 37 en memoria y el ATEC/Soyea como 0x7837; el decodificador del Konka pone el 1.er byte en la parte alta).
/// </summary>
public enum ClientCodeFormat
{
    /// <summary>Dato de 2 bytes en orden inverso al de envío: b2 b1 (LG 0x39DEFE, Konka 0x38FFA2).</summary>
    BytesInvertidos,

    /// <summary>
    /// Inmediato de la instrucción AEON "bg.sfeqi rA,Y" (ATEC: 0x3233D5): Y = B2·256 + B1. Y tiene signo
    /// (16 bits) y el valor recibido se compara sin signo, así que solo sirven códigos con B2 ≤ 0x7F.
    /// </summary>
    AeonSfeqi,
}

/// <summary>
/// Copia de un byte del código de cliente que el firmware escribe en el receptor IR por hardware (registros
/// 0x3DA4 = 1.er byte, 0x3DA5 = 2.º byte). Es el filtro que usa el chip EN REPOSO para saber qué mando lo enciende.
/// En el ATEC está fijo en una instrucción "bn.ori rD,r0,N" (3 bytes); en LG y Konka se calcula a partir del campo
/// principal, así que no necesitan campos de reposo.
/// </summary>
/// <param name="Which">1 = primer byte enviado (B1), 2 = segundo (B2).</param>
public sealed record ClientStandbyField(uint Address, int Which, int Register);

/// <summary>Dónde y cómo está el código de cliente del mando en un firmware.</summary>
public sealed class ClientCodeSpec
{
    public required uint Address { get; init; }
    public required ClientCodeFormat Format { get; init; }
    /// <summary>Solo AeonSfeqi: registro rA de la instrucción.</summary>
    public int Register { get; init; }
    /// <summary>El del firmware original.</summary>
    public required NecCustomer Original { get; init; }
    /// <summary>Copias del código para el encendido desde reposo (ver <see cref="ClientStandbyField"/>).</summary>
    public IReadOnlyList<ClientStandbyField> Standby { get; init; } = [];

    public const int StandbyFieldLength = 3;

    public byte[] EncodeStandby(ClientStandbyField f, NecCustomer c) => EncodeOri(f.Register, f.Which == 1 ? c.B1 : c.B2);

    /// <summary>Byte del código contenido en un campo de reposo (null si no es "bn.ori rD,r0,N").</summary>
    public static byte? DecodeStandby(ClientStandbyField f, ReadOnlySpan<byte> b)
    {
        if (b.Length != StandbyFieldLength) return null;
        uint w = (uint)(b[0] << 16 | b[1] << 8 | b[2]);
        if (w >> 18 != 0x14 || ((w >> 13) & 31) != f.Register || ((w >> 8) & 31) != 0) return null;
        return (byte)w;
    }

    /// <summary>
    /// bn.ori rD,rA,N (aeon_isa.json, AEON R2): "010 100 DDDDD AAAAA NNNNNNNN", 3 bytes big-endian; N sin signo.
    /// Aquí siempre rA = r0, es decir, rD = N.
    /// </summary>
    public static byte[] EncodeOri(int reg, byte imm)
    {
        if (reg is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(reg));
        uint w = (0x14u << 18) | ((uint)reg << 13) | imm;
        return [(byte)(w >> 16), (byte)(w >> 8), (byte)w];
    }

    public int ByteLength => Format == ClientCodeFormat.AeonSfeqi ? 4 : 2;

    public byte[] OriginalBytes => Encode(Original);

    /// <summary>Null si el código se puede poner en este firmware; si no, el motivo.</summary>
    public string? WhyNot(NecCustomer c) => Format == ClientCodeFormat.AeonSfeqi && c.B2 > 0x7F
        ? $"Este firmware compara el código de cliente con una instrucción que solo admite valores hasta 0x7FFF; el segundo byte ({c.B2:X2}) debe ser 7F o menor."
        : null;

    public byte[] Encode(NecCustomer c)
    {
        if (WhyNot(c) is { } why) throw new ArgumentException(why);
        return Format switch
        {
            ClientCodeFormat.BytesInvertidos => [c.B2, c.B1],
            _ => EncodeSfeqi(Register, (ushort)((c.B2 << 8) | c.B1)),
        };
    }

    /// <summary>Lee el código de los bytes del firmware (null si no tienen la forma esperada).</summary>
    public NecCustomer? Decode(ReadOnlySpan<byte> b)
    {
        if (b.Length != ByteLength) return null;
        switch (Format)
        {
            case ClientCodeFormat.BytesInvertidos: return new NecCustomer(b[1], b[0]);
            default:
                uint w = BinaryPrimitives.ReadUInt32BigEndian(b);
                if ((w & 0xFC00_001F) != 0xC000_0000 || ((w >> 21) & 31) != Register) return null;
                ushort y = (ushort)(w >> 5);
                return y > 0x7FFF ? null : new NecCustomer((byte)y, (byte)(y >> 8));
        }
    }

    /// <summary>
    /// bg.sfeqi rA,Y (aeon_isa.json, AEON R2): "110 000 AAAAA YYYYYYYYYYYYYYYY - 000 0", 4 bytes big-endian.
    /// </summary>
    public static byte[] EncodeSfeqi(int reg, ushort imm)
    {
        if (reg is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(reg));
        if (imm > 0x7FFF) throw new ArgumentOutOfRangeException(nameof(imm), "Y tiene signo: máximo 0x7FFF.");
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, 0xC000_0000u | ((uint)reg << 21) | ((uint)imm << 5));
        return b;
    }
}

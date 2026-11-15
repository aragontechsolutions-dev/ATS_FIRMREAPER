namespace FirmReaper.Core.Imaging;

/// <summary>
/// Lectura de la estructura de un JPEG (solo marcadores, sin decodificar). Trata el archivo como dato no
/// confiable: comprueba límites en cada lectura y lanza <see cref="InvalidDataException"/> si algo no cuadra.
/// </summary>
public sealed class JpegInfo
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Marcador SOF (0xC0 baseline, 0xC1 secuencial extendido, 0xC2 progresivo…).</summary>
    public required byte SofMarker { get; init; }
    public required int Components { get; init; }
    /// <summary>True si todos los componentes son 1x1 (4:4:4).</summary>
    public required bool Is444 { get; init; }
    public required int RestartInterval { get; init; }
    /// <summary>Tablas de cuantización de 8 bits en ORDEN NATURAL, por id.</summary>
    public required IReadOnlyDictionary<int, int[]> QuantTables { get; init; }
    /// <summary>Tabla de cuantización de cada componente (en orden de aparición).</summary>
    public required IReadOnlyList<int> ComponentQuantTable { get; init; }
    /// <summary>Longitud hasta el EOI (FFD9) incluido.</summary>
    public required int Length { get; init; }

    public bool IsBaseline => SofMarker is 0xC0 or 0xC1;

    public static JpegInfo Parse(ReadOnlySpan<byte> j)
    {
        static InvalidDataException Bad(string m) => new("JPEG no válido: " + m);
        if (j.Length < 4 || j[0] != 0xFF || j[1] != 0xD8)
            throw Bad("no empieza por FFD8.");

        int i = 2, width = 0, height = 0, comps = 0, restart = 0;
        byte sof = 0;
        bool is444 = true;
        var qt = new Dictionary<int, int[]>();
        var compQ = new List<int>();
        bool sawSos = false;

        while (true)
        {
            if (i + 4 > j.Length) throw Bad("termina antes de tiempo.");
            if (j[i] != 0xFF) throw Bad($"se esperaba un marcador en {i}.");
            byte m = j[i + 1];
            if (m == 0xFF) { i++; continue; } // relleno
            if (m is 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
            if (m == 0xD9) throw Bad("EOI antes de los datos.");
            int len = (j[i + 2] << 8) | j[i + 3];
            if (len < 2 || i + 2 + len > j.Length) throw Bad($"segmento 0x{m:X2} fuera de límites.");
            var seg = j.Slice(i + 4, len - 2);

            if (m is >= 0xC0 and <= 0xCF && m is not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (sof != 0) throw Bad("más de un SOF.");
                if (seg.Length < 6) throw Bad("SOF corto.");
                if (seg[0] != 8) throw Bad("solo se admiten 8 bits por muestra.");
                sof = m;
                height = (seg[1] << 8) | seg[2];
                width = (seg[3] << 8) | seg[4];
                comps = seg[5];
                if (seg.Length < 6 + comps * 3) throw Bad("SOF incompleto.");
                for (int c = 0; c < comps; c++)
                {
                    if (seg[6 + c * 3 + 1] != 0x11) is444 = false;
                    compQ.Add(seg[6 + c * 3 + 2]);
                }
            }
            else if (m == 0xDB)
            {
                int k = 0;
                while (k < seg.Length)
                {
                    int pq = seg[k] >> 4, id = seg[k] & 15;
                    if (pq != 0) throw Bad("tablas de cuantización de 16 bits no admitidas.");
                    if (k + 65 > seg.Length) throw Bad("DQT incompleto.");
                    var t = new int[64];
                    for (int z = 0; z < 64; z++) t[JpegBaselineEncoder.NaturalOrder[z]] = seg[k + 1 + z];
                    qt[id] = t;
                    k += 65;
                }
            }
            else if (m == 0xDD)
            {
                if (seg.Length < 2) throw Bad("DRI corto.");
                restart = (seg[0] << 8) | seg[1];
            }
            else if (m == 0xDA)
            {
                sawSos = true;
                i += 2 + len;
                break;
            }
            i += 2 + len;
        }
        if (!sawSos || sof == 0) throw Bad("faltan SOF o SOS.");
        if (width < 1 || height < 1) throw Bad("dimensiones nulas.");

        // Datos entrópicos hasta EOI (saltando bytes rellenos FF00 y marcadores RST).
        int end = -1;
        for (int p = i; p + 1 < j.Length; p++)
        {
            if (j[p] != 0xFF) continue;
            byte n = j[p + 1];
            if (n == 0x00 || n is >= 0xD0 and <= 0xD7 || n == 0xFF) continue;
            if (n == 0xD9) { end = p + 2; break; }
            // Escaneos adicionales (progresivo) u otros marcadores: se aceptan y se sigue buscando EOI.
        }
        if (end < 0) throw Bad("no se encontró el EOI (FFD9).");

        foreach (int q in compQ)
            if (!qt.ContainsKey(q)) throw Bad($"falta la tabla de cuantización {q}.");

        return new JpegInfo
        {
            Width = width,
            Height = height,
            SofMarker = sof,
            Components = comps,
            Is444 = is444,
            RestartInterval = restart,
            QuantTables = qt,
            ComponentQuantTable = compQ,
            Length = end,
        };
    }
}

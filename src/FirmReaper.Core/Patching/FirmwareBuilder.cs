using System.Buffers.Binary;
using System.Security.Cryptography;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.Compression;
using FirmReaper.Core.Compression.Zopfli;
using FirmReaper.Core.Imaging;
using FirmReaper.Core.MStar;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Patching;

/// <summary>Resultado de generar un firmware. Solo existe si TODAS las comprobaciones pasaron.</summary>
public sealed class BuildResult
{
    public required Profile Profile { get; init; }
    public required IReadOnlyDictionary<string, string> Selection { get; init; }
    public required IReadOnlyList<Patch> Patches { get; init; }
    public required byte[] Output { get; init; }
    public required string InputSha256 { get; init; }
    public required string OutputSha256 { get; init; }
    public required AppHeader OldHeader { get; init; }
    public required AppHeader NewHeader { get; init; }
    public required int CompressedSize { get; init; }
    public required long FreeBytes { get; init; }
    public required int ChangedAppBytes { get; init; }
    public required LogoAction Logo { get; init; }
    /// <summary>SHA-256 y tamaño del JPEG escrito en el hueco del logo (null si el logo no cambia).</summary>
    public string? LogoSha256 { get; init; }
    public int LogoLength { get; init; }
    /// <summary>Salida conocida del perfil para esta selección (null si no hay).</summary>
    public KnownOutput? Known { get; init; }
    /// <summary>True si la huella coincide con la salida conocida (archivo idéntico al ya generado/probado).</summary>
    public bool MatchesKnown => Known is not null && Known.Sha256 == OutputSha256;
    public required IReadOnlyList<string> Verifications { get; init; }
}

/// <summary>Error de generación: se lanza ANTES de producir ningún archivo.</summary>
public sealed class BuildException(string message) : Exception(message);

/// <summary>
/// Genera un firmware modificado según un perfil. Pasos (cada uno debe superarse o no hay salida):
/// 1) contenedor válido y huella = original del perfil; 2) bytes originales en cada parche;
/// 3) aplicar; 4) recomprimir con Zopfli; 5) cabe antes de los recursos; 6) nueva cabecera y CRC;
/// 7) logo (girar o propio) validado; 8) re-verificar la salida y que solo cambió lo previsto.
/// </summary>
public static class FirmwareBuilder
{
    /// <summary>Tamaño máximo aceptado para un JPEG propio leído de disco.</summary>
    public const int MaxCustomLogoBytes = 4 * 1024 * 1024;

    public static BuildResult Build(byte[] fw, Profile profile, IReadOnlyDictionary<string, string> selection,
        byte[]? customLogo = null, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(fw);
        ArgumentNullException.ThrowIfNull(profile);
        var verifications = new List<string>();
        void Ok(string s)
        {
            verifications.Add(s);
            progress?.Report(s);
        }

        // 1. Entrada
        progress?.Report("Analizando el original…");
        var a = FirmwareAnalyzer.Analyze(fw);
        if (!a.ContainerValid || a.App is null || a.Header is null || a.Tel is null || a.Tail is null || a.DeflateEnd is null)
            throw new BuildException("El archivo de entrada no supera las comprobaciones del contenedor.");
        if (a.Sha256 != profile.OriginalSha256)
            throw new BuildException($"La huella del archivo ({a.Sha256[..16]}…) no es la del original del perfil ({profile.OriginalSha256[..16]}…).");
        Ok($"Huella del original correcta ({a.Sha256}).");
        if (!ProfileLoader.BaseMatches(profile, a.Header))
            throw new BuildException($"La base de la app del perfil (0x{profile.AppBaseRam:X}) no coincide con la del firmware (0x{a.Header.RamDest + MStarLayout.UncompressedPrefix:X}).");

        var patches = profile.PatchesFor(selection);
        var logoAction = profile.LogoActionFor(selection);
        if (patches.Count == 0 && logoAction == LogoAction.Ninguna)
            throw new BuildException("Con estas opciones no hay nada que cambiar: el resultado sería el original.");
        if (logoAction != LogoAction.Propio && customLogo is not null)
            throw new BuildException("Se indicó un logo propio, pero la opción elegida no usa un logo propio.");

        var outFw = (byte[])fw.Clone();
        var h = a.Header;
        var nh = h;
        uint limit = a.Tel.FirstResourceOffset;
        int compressedSize = a.DeflateEnd.Value - MStarLayout.DeflateOffset;
        long newEnd = h.FlashEnd;
        var app = a.App;
        var newApp = app;

        if (patches.Count > 0)
        {
            // 2. Bytes originales
            foreach (var p in patches)
            {
                long off = (long)p.Address - profile.AppBaseRam;
                if (off < 0 || off + p.Original.Length > app.Length)
                    throw new BuildException($"Parche 0x{p.Address:X} fuera de la aplicación.");
                var cur = app.AsSpan((int)off, p.Original.Length);
                if (!cur.SequenceEqual(p.Original))
                    throw new BuildException(cur.SequenceEqual(p.New)
                        ? $"Parche 0x{p.Address:X}: el archivo YA tiene este cambio."
                        : $"Parche 0x{p.Address:X}: los bytes originales no coinciden.");
            }
            Ok($"Bytes originales correctos en los {patches.Count} parches.");

            // 3. Aplicar (sobre una copia)
            newApp = (byte[])app.Clone();
            foreach (var p in patches)
                p.New.CopyTo(newApp, (int)(p.Address - profile.AppBaseRam));

            // 4. Recomprimir
            cancel.ThrowIfCancellationRequested();
            progress?.Report($"Comprimiendo con Zopfli ({profile.ZopfliIterations} iteraciones)…");
            var comp = ZopfliDeflater.Compress(newApp, new ZopfliOptions(NumIterations: profile.ZopfliIterations));
            cancel.ThrowIfCancellationRequested();
            if (!RawInflater.Inflate(comp, MStarLayout.MaxAppSize).Data.AsSpan().SequenceEqual(newApp))
                throw new BuildException("La recompresión no es reversible.");
            Ok($"Recompresión reversible ({Fmt.N(comp.Length)} bytes).");

            // 5. ¿Cabe?
            var newTail = new byte[MStarLayout.TailSize];
            BinaryPrimitives.WriteUInt32LittleEndian(newTail, (uint)newApp.Length);
            newTail[4] = a.Tail[4];
            newTail[5] = a.Tail[5];
            newEnd = (long)MStarLayout.DeflateOffset + comp.Length + newTail.Length;
            if (newEnd > limit)
                throw new BuildException($"La app recomprimida no cabe: termina en 0x{newEnd:X} y el primer recurso está en 0x{limit:X}.");
            Ok($"Cabe: fin 0x{newEnd:X6}, primer recurso 0x{limit:X6}, libres {Fmt.N(limit - newEnd)} bytes.");

            // 6. Montar la app y la cabecera
            outFw.AsSpan(MStarLayout.DeflateOffset, (int)(limit - MStarLayout.DeflateOffset)).Clear();
            comp.CopyTo(outFw, MStarLayout.DeflateOffset);
            newTail.CopyTo(outFw, MStarLayout.DeflateOffset + comp.Length);
            uint crc = Crc32Msb.Compute(outFw.AsSpan(MStarLayout.AppFlashStart, (int)(newEnd - MStarLayout.AppFlashStart)));
            nh = h with
            {
                FlashEnd = (uint)newEnd,
                RamEnd = h.RamDest + (uint)(newEnd - MStarLayout.AppFlashStart),
                Crc = crc,
            };
            var hs = outFw.AsSpan(MStarLayout.HeaderOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(hs[0x08..], nh.RamEnd);
            BinaryPrimitives.WriteUInt32LittleEndian(hs[0x0C..], nh.FlashEnd);
            BinaryPrimitives.WriteUInt32LittleEndian(hs[0x14..], nh.Crc);
            compressedSize = comp.Length;
        }
        else
        {
            Ok("La aplicación no cambia (solo el logo): no se recomprime.");
        }

        // 7. Logo
        byte[]? newLogo = null;
        int logoOffset = 0, logoSlot = 0;
        if (logoAction != LogoAction.Ninguna)
        {
            var spec = profile.Logo ?? throw new BuildException("El perfil no describe el recurso del logo.");
            var entry = a.Tel.Entries.FirstOrDefault(e => e.Id == spec.TelId)
                ?? throw new BuildException($"No existe el recurso 0x{spec.TelId:X4} (logo) en la tabla TEL.");
            if (entry.Length < spec.JpegLength || entry.Offset < limit)
                throw new BuildException("El recurso del logo no tiene el tamaño o la posición esperados.");
            logoOffset = (int)entry.Offset;
            logoSlot = spec.JpegLength;
            var orig = fw.AsSpan(logoOffset, spec.JpegLength).ToArray();
            JpegInfo oi;
            try
            {
                oi = JpegInfo.Parse(orig);
            }
            catch (InvalidDataException ex)
            {
                throw new BuildException("El logo original no es el esperado: " + ex.Message);
            }
            if (oi.Length != spec.JpegLength || oi.Width != spec.Width || oi.Height != spec.Height)
                throw new BuildException("El logo original no es el esperado (longitud o dimensiones).");

            cancel.ThrowIfCancellationRequested();
            if (logoAction == LogoAction.Girar)
            {
                progress?.Report("Girando el logo 180°…");
                try
                {
                    newLogo = LogoImage.Rotate180(orig);
                }
                catch (InvalidDataException ex)
                {
                    throw new BuildException("No se pudo girar el logo: " + ex.Message);
                }
            }
            else
            {
                newLogo = customLogo ?? throw new BuildException("Falta el archivo JPEG del logo propio.");
            }
            ValidateLogo(newLogo, spec);
            Ok($"Logo {(logoAction == LogoAction.Girar ? "girado 180°" : "propio")} válido: JPEG baseline {spec.Width}x{spec.Height}, " +
               $"{Fmt.N(newLogo.Length)} bytes (máximo {Fmt.N(spec.JpegLength)}).");

            // Mismo formato que el script: JPEG + ceros hasta la longitud original; el resto del recurso se conserva.
            outFw.AsSpan(logoOffset, logoSlot).Clear();
            newLogo.CopyTo(outFw, logoOffset);
        }

        // 8. Verificación independiente de la salida
        progress?.Report("Verificando el archivo generado…");
        var b = FirmwareAnalyzer.Analyze(outFw);
        var errors = b.Checks.Where(c => c.Status == CheckStatus.Error).Select(c => c.Title + ": " + c.Detail).ToList();
        if (errors.Count > 0)
            throw new BuildException("La salida no supera las comprobaciones: " + string.Join("; ", errors));
        Ok($"Contenedor de salida válido (CRC 0x{nh.Crc:X8}).");
        if (b.App is null || !b.App.AsSpan().SequenceEqual(newApp))
            throw new BuildException("La app descomprimida de la salida no es la esperada.");

        // Solo pueden cambiar los bytes de los parches, y con el valor nuevo.
        var allowed = new bool[app.Length];
        foreach (var p in patches)
            for (int i = 0; i < p.Original.Length; i++) allowed[p.Address - profile.AppBaseRam + i] = true;
        int changed = 0;
        for (int i = 0; i < app.Length; i++)
        {
            if (app[i] == b.App[i]) continue;
            if (!allowed[i])
                throw new BuildException($"Cambio NO previsto en la app (0x{profile.AppBaseRam + i:X}).");
            changed++;
        }
        foreach (var p in patches)
            if (!b.App.AsSpan((int)(p.Address - profile.AppBaseRam), p.New.Length).SequenceEqual(p.New))
                throw new BuildException($"El parche 0x{p.Address:X} no quedó aplicado.");
        if (patches.Count > 0)
            Ok($"Solo cambiaron los bytes previstos ({changed} bytes en la app).");

        // Fuera de la app el archivo debe ser idéntico, salvo el hueco del logo (si se cambió).
        bool SameRange(int from, int to) => fw.AsSpan(from, to - from).SequenceEqual(outFw.AsSpan(from, to - from));
        bool outsideOk = SameRange(0, MStarLayout.HeaderOffset)
            && SameRange(MStarLayout.HeaderOffset, MStarLayout.HeaderOffset + 8)
            && SameRange(MStarLayout.HeaderOffset + 0x10, MStarLayout.HeaderOffset + 0x14)
            && SameRange(MStarLayout.HeaderOffset + AppHeader.Size, MStarLayout.DeflateOffset);
        if (newLogo is null)
        {
            outsideOk &= SameRange((int)limit, fw.Length);
        }
        else
        {
            outsideOk &= SameRange((int)limit, logoOffset) && SameRange(logoOffset + logoSlot, fw.Length);
            var slot = outFw.AsSpan(logoOffset, logoSlot);
            if (!slot[..newLogo.Length].SequenceEqual(newLogo) || slot[newLogo.Length..].IndexOfAnyExcept((byte)0) >= 0)
                throw new BuildException("El hueco del logo no quedó como se esperaba.");
        }
        if (!outsideOk)
            throw new BuildException("Hay cambios fuera de las zonas previstas.");
        Ok(newLogo is null
            ? "Fuera de la aplicación el archivo es idéntico al original (SBOOT, PM, MS_IMG_INFO, TEL, recursos, base de datos)."
            : "Fuera de la aplicación y del hueco del logo el archivo es idéntico al original.");

        string outSha = Convert.ToHexStringLower(SHA256.HashData(outFw));
        var known = profile.KnownOutputFor(selection);
        if (known is not null)
            Ok(known.Sha256 == outSha
                ? $"Idéntico a la versión ya generada y documentada ({known.FileName})."
                : $"AVISO: difiere de la versión documentada {known.FileName}.");

        return new BuildResult
        {
            Profile = profile,
            Selection = profile.Options.ToDictionary(o => o.Id, o => selection.TryGetValue(o.Id, out var v) ? v : o.Default),
            Patches = patches,
            Output = outFw,
            InputSha256 = a.Sha256,
            OutputSha256 = outSha,
            OldHeader = h,
            NewHeader = nh,
            CompressedSize = compressedSize,
            FreeBytes = limit - newEnd,
            ChangedAppBytes = changed,
            Logo = logoAction,
            LogoSha256 = newLogo is null ? null : Convert.ToHexStringLower(SHA256.HashData(newLogo)),
            LogoLength = newLogo?.Length ?? 0,
            Known = known,
            Verifications = verifications,
        };
    }

    /// <summary>
    /// Comprueba un JPEG para el hueco del logo: baseline (no progresivo), dimensiones exactas, en color,
    /// que termina en EOI sin basura detrás, que cabe, y que se decodifica entero.
    /// </summary>
    public static void ValidateLogo(byte[] jpeg, LogoSpec spec)
    {
        JpegInfo info;
        try
        {
            info = JpegInfo.Parse(jpeg);
        }
        catch (InvalidDataException ex)
        {
            throw new BuildException("Logo: " + ex.Message);
        }
        if (!info.IsBaseline)
            throw new BuildException("Logo: debe ser JPEG baseline (no progresivo).");
        if (info.Width != spec.Width || info.Height != spec.Height)
            throw new BuildException($"Logo: debe medir {spec.Width}x{spec.Height} (mide {info.Width}x{info.Height}).");
        if (info.Components != 3)
            throw new BuildException("Logo: debe ser en color (3 componentes).");
        if (info.Length != jpeg.Length)
            throw new BuildException("Logo: hay datos después del final del JPEG (FFD9).");
        if (jpeg.Length > spec.JpegLength)
            throw new BuildException($"Logo: ocupa {Fmt.N(jpeg.Length)} bytes; el máximo es {Fmt.N(spec.JpegLength)}.");
        try
        {
            LogoImage.DecodeRgb(jpeg, out _, out _);
        }
        catch (InvalidDataException ex)
        {
            throw new BuildException("Logo: " + ex.Message);
        }
    }
}

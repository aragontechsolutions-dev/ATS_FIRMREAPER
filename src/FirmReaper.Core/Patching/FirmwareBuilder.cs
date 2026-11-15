using System.Buffers.Binary;
using System.Security.Cryptography;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.Compression;
using FirmReaper.Core.Compression.Zopfli;
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
/// 7) re-verificar la salida y comprobar que solo cambió lo previsto.
/// </summary>
public static class FirmwareBuilder
{
    public static BuildResult Build(byte[] fw, Profile profile, IReadOnlyDictionary<string, string> selection,
        IProgress<string>? progress = null, CancellationToken cancel = default)
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
        if (patches.Count == 0)
            throw new BuildException("Con estas opciones no hay nada que cambiar: el resultado sería el original.");

        // 2. Bytes originales
        var app = a.App;
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
        var newApp = (byte[])app.Clone();
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
        uint limit = a.Tel.FirstResourceOffset;
        var newTail = new byte[MStarLayout.TailSize];
        BinaryPrimitives.WriteUInt32LittleEndian(newTail, (uint)newApp.Length);
        newTail[4] = a.Tail[4];
        newTail[5] = a.Tail[5];
        long newEnd = (long)MStarLayout.DeflateOffset + comp.Length + newTail.Length;
        if (newEnd > limit)
            throw new BuildException($"La app recomprimida no cabe: termina en 0x{newEnd:X} y el primer recurso está en 0x{limit:X}.");
        Ok($"Cabe: fin 0x{newEnd:X6}, primer recurso 0x{limit:X6}, libres {Fmt.N(limit - newEnd)} bytes.");

        // 6. Montar la salida
        var outFw = (byte[])fw.Clone();
        outFw.AsSpan(MStarLayout.DeflateOffset, (int)(limit - MStarLayout.DeflateOffset)).Clear();
        comp.CopyTo(outFw, MStarLayout.DeflateOffset);
        newTail.CopyTo(outFw, MStarLayout.DeflateOffset + comp.Length);
        var h = a.Header;
        uint crc = Crc32Msb.Compute(outFw.AsSpan(MStarLayout.AppFlashStart, (int)(newEnd - MStarLayout.AppFlashStart)));
        var nh = h with
        {
            FlashEnd = (uint)newEnd,
            RamEnd = h.RamDest + (uint)(newEnd - MStarLayout.AppFlashStart),
            Crc = crc,
        };
        var hs = outFw.AsSpan(MStarLayout.HeaderOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(hs[0x08..], nh.RamEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(hs[0x0C..], nh.FlashEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(hs[0x14..], nh.Crc);

        // 7. Verificación independiente de la salida
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
        Ok($"Solo cambiaron los bytes previstos ({changed} bytes en la app).");

        // Fuera de la zona de la app el archivo debe ser idéntico.
        if (!fw.AsSpan(0, MStarLayout.HeaderOffset).SequenceEqual(outFw.AsSpan(0, MStarLayout.HeaderOffset))
            || !fw.AsSpan(MStarLayout.HeaderOffset, 8).SequenceEqual(outFw.AsSpan(MStarLayout.HeaderOffset, 8))
            || !fw.AsSpan(MStarLayout.HeaderOffset + 0x10, 4).SequenceEqual(outFw.AsSpan(MStarLayout.HeaderOffset + 0x10, 4))
            || !fw.AsSpan(MStarLayout.HeaderOffset + AppHeader.Size, MStarLayout.DeflateOffset - MStarLayout.HeaderOffset - AppHeader.Size)
                   .SequenceEqual(outFw.AsSpan(MStarLayout.HeaderOffset + AppHeader.Size, MStarLayout.DeflateOffset - MStarLayout.HeaderOffset - AppHeader.Size))
            || !fw.AsSpan((int)limit).SequenceEqual(outFw.AsSpan((int)limit)))
            throw new BuildException("Hay cambios fuera de la zona de la aplicación.");
        Ok("Fuera de la aplicación el archivo es idéntico al original (SBOOT, PM, MS_IMG_INFO, TEL, recursos, base de datos).");

        string outSha = Convert.ToHexStringLower(SHA256.HashData(outFw));
        var known = profile.KnownOutputFor(selection);
        if (known is not null)
            Ok(known.Sha256 == outSha
                ? $"Idéntico a la versión ya generada y documentada ({known.FileName})."
                : $"AVISO: difiere de la versión documentada {known.FileName} (mismo contenido, compresión distinta).");

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
            CompressedSize = comp.Length,
            FreeBytes = limit - newEnd,
            ChangedAppBytes = changed,
            Known = known,
            Verifications = verifications,
        };
    }
}

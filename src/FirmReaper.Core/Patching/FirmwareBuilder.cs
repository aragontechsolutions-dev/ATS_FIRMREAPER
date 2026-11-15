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
    /// <summary>Dónde se escribió el logo y cómo se obtuvo (null si el logo no cambia).</summary>
    public string? LogoDetail { get; init; }
    /// <summary>Tabla del mando escrita (null si el perfil no tiene tabla o la opción no la aplica).</summary>
    public IReadOnlyList<KeyAssignment>? KeyMap { get; init; }
    /// <summary>True si la tabla del mando es distinta de la del perfil.</summary>
    public bool KeyMapEdited { get; init; }
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
/// 7) logo (girar o propio) en el formato del original y validado; 8) re-verificar la salida y que solo cambió lo previsto.
/// </summary>
public static class FirmwareBuilder
{
    /// <summary>Tamaño máximo aceptado para la imagen del logo propio leída de disco (JPEG, PNG o BMP).</summary>
    public const int MaxCustomLogoBytes = 64 * 1024 * 1024;

    public static BuildResult Build(byte[] fw, Profile profile, IReadOnlyDictionary<string, string> selection,
        byte[]? customLogo = null, IProgress<string>? progress = null, CancellationToken cancel = default,
        IReadOnlyList<KeyAssignment>? keyMap = null)
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

        // Tabla del mando: si se aplica la opción que la contiene, se puede sustituir por una editada.
        IReadOnlyList<KeyAssignment>? appliedKeyMap = null;
        bool keyMapEdited = false;
        var kt = profile.KeyTable;
        bool tableApplies = kt is not null &&
            (selection.TryGetValue(kt.OptionId, out var tv) ? tv : profile.Options.First(o => o.Id == kt.OptionId).Default) == kt.ValueId;
        if (keyMap is not null && !tableApplies)
            throw new BuildException("Se indicó una tabla del mando, pero la opción elegida no la aplica.");
        if (tableApplies)
        {
            appliedKeyMap = keyMap ?? kt!.Default;
            var errs = kt!.Validate(appliedKeyMap);
            if (errs.Count > 0)
                throw new BuildException("Tabla del mando no válida: " + string.Join(" ", errs));
            keyMapEdited = !kt.IsDefault(appliedKeyMap);
            if (keyMapEdited)
            {
                int i = patches.FindIndex(p => p.Address == kt.Address);
                var old = patches[i];
                patches[i] = old with { New = kt.Encode(appliedKeyMap), Description = old.Description + " [EDITADA]" };
                Ok($"Tabla del mando editada: {appliedKeyMap.Count} teclas (máximo {kt.MaxAssignments}).");
            }
        }
        if (patches.Count == 0 && logoAction == LogoAction.Ninguna)
            throw new BuildException("Con estas opciones no hay nada que cambiar: el resultado sería el original.");
        if (logoAction != LogoAction.Propio && customLogo is not null)
            throw new BuildException("Se indicó una imagen para el logo, pero la opción elegida no usa un logo propio.");

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
            progress?.Report($"Comprimiendo ({profile.Compression})…");
            var comp = profile.Compression.Method == CompressionMethod.Zopfli
                ? ZopfliDeflater.Compress(newApp, new ZopfliOptions(NumIterations: profile.Compression.Value))
                : ZlibDeflater.Compress(newApp, profile.Compression.Value);
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
        string? logoDetail = null;
        LogoSlot? slot = null;
        if (logoAction != LogoAction.Ninguna)
        {
            var spec = profile.Logo ?? throw new BuildException("El perfil no describe el logo.");
            slot = ResolveLogoSlot(fw, a.Tel, spec, limit);
            Ok($"Logo original correcto en {spec.Location}: JPEG {slot.Format.Description}, {Fmt.N(spec.JpegLength)} bytes; " +
               $"hueco de {Fmt.N(slot.Length)} bytes.");

            cancel.ThrowIfCancellationRequested();
            try
            {
                LogoEncoding enc;
                if (logoAction == LogoAction.Girar)
                {
                    progress?.Report("Girando el logo 180°…");
                    enc = LogoImage.Rotated(slot.Original, slot.Length);
                }
                else
                {
                    progress?.Report("Preparando el logo propio…");
                    enc = LogoImage.FromUserImage(customLogo ?? throw new BuildException("Falta la imagen del logo propio."),
                        slot.Format, slot.Length);
                }
                newLogo = enc.Jpeg;
                logoDetail = $"{spec.Location}: {enc.Detail}";
            }
            catch (InvalidDataException ex)
            {
                throw new BuildException("Logo: " + ex.Message);
            }
            ValidateLogo(newLogo, slot.Format, slot.Length);
            Ok($"Logo {(logoAction == LogoAction.Girar ? "girado 180°" : "propio")} válido: {logoDetail}");

            // JPEG + relleno hasta el final del hueco; con CRC-16, se recalcula al final del recurso.
            outFw.AsSpan(slot.Offset, slot.Length).Fill(slot.Fill);
            newLogo.CopyTo(outFw, slot.Offset);
            if (slot.CrcLength > 0)
            {
                Crc16Tel.Update(outFw.AsSpan(slot.Offset, slot.CrcLength));
                Ok($"CRC-16 del recurso actualizado (0x{BinaryPrimitives.ReadUInt16BigEndian(outFw.AsSpan(slot.Offset + slot.CrcLength - 2)):X4}).");
            }
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
        if (slot is null)
        {
            outsideOk &= SameRange((int)limit, fw.Length);
        }
        else
        {
            int slotEnd = slot.Offset + Math.Max(slot.Length, slot.CrcLength);
            outsideOk &= SameRange((int)limit, slot.Offset) && SameRange(slotEnd, fw.Length);
            var written = outFw.AsSpan(slot.Offset, slot.Length);
            if (!written[..newLogo!.Length].SequenceEqual(newLogo) || written[newLogo.Length..].IndexOfAnyExcept(slot.Fill) >= 0)
                throw new BuildException("El hueco del logo no quedó como se esperaba.");
            if (slot.CrcLength > 0 && !Crc16Tel.IsValid(outFw.AsSpan(slot.Offset, slot.CrcLength)))
                throw new BuildException("El CRC-16 del recurso del logo no quedó correcto.");
        }
        if (!outsideOk)
            throw new BuildException("Hay cambios fuera de las zonas previstas.");
        Ok(newLogo is null
            ? "Fuera de la aplicación el archivo es idéntico al original (SBOOT, PM, MS_IMG_INFO, TEL, recursos, base de datos)."
            : "Fuera de la aplicación y del hueco del logo el archivo es idéntico al original.");

        string outSha = Convert.ToHexStringLower(SHA256.HashData(outFw));
        // Con una tabla editada no hay versión documentada con la que comparar.
        var known = keyMapEdited ? null : profile.KnownOutputFor(selection);
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
            LogoDetail = logoDetail,
            KeyMap = appliedKeyMap,
            KeyMapEdited = keyMapEdited,
            Known = known,
            Verifications = verifications,
        };
    }

    /// <summary>Hueco del logo en el archivo: [Offset, Offset+Length) se rellena con Fill y recibe el JPEG.</summary>
    /// <param name="CrcLength">Si &gt; 0: longitud del recurso TEL cuyo CRC-16 (2 últimos bytes) hay que recalcular.</param>
    private sealed record LogoSlot(int Offset, int Length, byte Fill, int CrcLength, LogoFormat Format, byte[] Original);

    /// <summary>
    /// Localiza el hueco del logo y comprueba que el original es exactamente el que describe el perfil:
    /// JPEG de la longitud y dimensiones indicadas, el resto del hueco con el relleno esperado y, si el recurso
    /// lleva CRC-16, que el CRC original sea correcto. Si algo no cuadra, no se toca nada.
    /// </summary>
    private static LogoSlot ResolveLogoSlot(byte[] fw, TelTable tel, LogoSpec spec, uint appLimit)
    {
        int offset, length, crcLength = 0;
        byte fill;
        if (spec.TelId is { } id)
        {
            var e = tel.Entries.FirstOrDefault(x => x.Id == id)
                ?? throw new BuildException($"No existe el recurso 0x{id:X4} (logo) en la tabla TEL.");
            if (e.Offset < appLimit || e.Length < spec.JpegLength + (spec.Crc16 ? 2 : 0))
                throw new BuildException("El recurso del logo no tiene el tamaño o la posición esperados.");
            offset = (int)e.Offset;
            fill = 0;
            if (spec.Crc16)
            {
                if (!Crc16Tel.IsValid(fw.AsSpan(offset, (int)e.Length)))
                    throw new BuildException($"El CRC-16 del recurso 0x{id:X4} no es correcto en el original.");
                crcLength = (int)e.Length;
                length = crcLength - 2;
            }
            else
            {
                length = spec.JpegLength;
            }
        }
        else
        {
            // Zona fija: después de la app, de todos los recursos y del área TEL declarada; sin solaparse con nada.
            uint telEnd = tel.Entries.Count == 0 ? 0 : tel.Entries.Max(e => e.Offset + e.Length);
            if (spec.FlashFrom < appLimit || spec.FlashFrom < telEnd || spec.FlashFrom < tel.ReservedSize || spec.FlashTo > fw.Length)
                throw new BuildException($"La zona del logo (0x{spec.FlashFrom:X6}) se solapa con la app o con los recursos TEL.");
            offset = (int)spec.FlashFrom;
            length = (int)(spec.FlashTo - spec.FlashFrom);
            fill = spec.Fill;
        }

        var original = fw.AsSpan(offset, spec.JpegLength).ToArray();
        JpegInfo info;
        LogoFormat fmt;
        try
        {
            info = JpegInfo.Parse(original);
            fmt = LogoFormat.FromJpeg(original);
        }
        catch (InvalidDataException ex)
        {
            throw new BuildException("El logo original no es el esperado: " + ex.Message);
        }
        if (info.Length != spec.JpegLength || info.Width != spec.Width || info.Height != spec.Height)
            throw new BuildException("El logo original no es el esperado (longitud o dimensiones).");
        if (fw.AsSpan(offset + spec.JpegLength, length - spec.JpegLength).IndexOfAnyExcept(fill) >= 0)
            throw new BuildException($"Tras el logo original hay datos distintos del relleno 0x{fill:X2}: no se modifica.");
        return new LogoSlot(offset, length, fill, crcLength, fmt, original);
    }

    /// <summary>
    /// Última comprobación del JPEG que se va a escribir: baseline, en color, mismas dimensiones y submuestreo
    /// que el original, nada tras el FFD9, que cabe y que se decodifica entero.
    /// </summary>
    public static void ValidateLogo(byte[] jpeg, LogoFormat format, int maxBytes)
    {
        if (LogoImage.WhyNotDirect(jpeg, format, maxBytes) is { } why)
            throw new BuildException("Logo: " + why + ".");
    }
}

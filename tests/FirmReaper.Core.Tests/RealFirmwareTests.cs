using FirmReaper.Core.Analysis;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.Compression.Zopfli;
using FirmReaper.Core.MStar;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Tests;

/// <summary>
/// Pruebas con los firmwares REALES. Los .BIN no se suben a git: se buscan en la carpeta indicada por
/// la variable de entorno FIRMREAPER_FW_DIR o, si no existe, en firmware_local/ del repositorio
/// (subcarpetas originales/ y referencia/). Si un archivo no está, la prueba se omite (no falla).
/// Los valores esperados son los que dan los scripts Python de tools/referencia_python.
/// </summary>
public class RealFirmwareTests
{
    private static string? FindFirmware(string name)
    {
        var roots = new List<string>();
        var env = Environment.GetEnvironmentVariable("FIRMREAPER_FW_DIR");
        if (!string.IsNullOrEmpty(env)) roots.Add(env);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FirmReaper.sln")))
            dir = dir.Parent;
        if (dir is not null) roots.Add(Path.Combine(dir.FullName, "firmware_local"));

        foreach (var r in roots)
            foreach (var sub in new[] { "", "originales", "referencia" })
            {
                var p = Path.Combine(r, sub, name);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    public static TheoryData<string, string, uint, uint, int, IdentificationKind> Casos => new()
    {
        // archivo, sha256, fin flash, CRC, tamaño app, identificación
        { "Atec_32_KPC_Salva.BIN", "19491133011eaca67db793dcfb068771e19a58dac42eff849ca30993fbad647b", 0x1E9283, 0xDC243619, 3_706_784, IdentificationKind.Conocido },
        { "MST_EXT_SPI_ISP_20220511_165639.BIN", "ef5c80550894357d7f7e6caf6367d8cf7f7a8931dd6c4757f2e01cd136bee871", 0x1850CB, 0xD64ADEF6, 0x2D47B8, IdentificationKind.Conocido },
        { "MST_EXT_SPI_ISP_20230301_153737.BIN", "fc9fed8fef551b17c7a2da420a212cd52ba5eb075e670e7f3389c123b600a21e", 0x1EAA7F, 0xB1B76255, 0x38E990, IdentificationKind.Conocido },
        { "LG_42LF652VX_MANDO_SOYEA_v5.BIN", "17918f8fa6607bb802fdf6c2e71f554b547a390201eb65b9e704494b9fd9b002", 0x17D6B3, 0x8102266F, 0x2D47B8, IdentificationKind.Conocido },
        { "LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN", "3a836aee3dacb7d60fade3f7e834e8578309156e58a02e365e122fe236fed599", 0x17D6B3, 0xD38845E4, 0x2D47B8, IdentificationKind.Conocido },
        { "Atec32L18D_LP141WX1_6bit.BIN", "7bd55c392efabf11ad37b61eff631792ba285bb82a1ce71d06bdee89976d65e2", 0x1E8B76, 0x79B20851, 3_706_784, IdentificationKind.Conocido },
        { "Atec32L18D_LP141WX1_8bit.BIN", "ab3d3485707e3c271e129ae9be633ad6ca3d63aa2051c5ef16596dbc727c918c", 0x1E8B6D, 0xA118D9F1, 3_706_784, IdentificationKind.Conocido },
        { "TV4-32K16.bin", "32c529658cdefd4beee9fd408a8d52586bbfa7cf4416084a7784f76dcadaad9e", 0x0F9021, 0x02393624, 1_866_152, IdentificationKind.Conocido },
        { "TV4-32K16_MIRROR_MENU.bin", "81bd3c8b643745d321a99f3a6479dee8dc97b464d55bbb3be006fb6160b93570", 0x0F498B, 0xC6BE5147, 1_866_152, IdentificationKind.Conocido },
        { "TV4-32K16_MIRROR.bin", "98a919fbef5f848147272fe37a6ccd51bbd0166c0eb22ae8a75125ef211e5fe0", 0x0F4901, 0x1B0592DB, 1_866_152, IdentificationKind.Conocido },
        { "TV4-32K16_MIRROR_MENU_SIN_LOGO.bin", "3bffdfd07255108fca0d1edc5cfa154f79e27375a3a4281d3284aa0d76750e59", 0x0F498B, 0xC6BE5147, 1_866_152, IdentificationKind.Conocido },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void AnalizaEIdentificaComoLosScriptsPython(string file, string sha, uint flashEnd, uint crc, int appSize, IdentificationKind kind)
    {
        var path = FindFirmware(file);
        if (path is null)
            Assert.Skip($"No está {file} (definir FIRMREAPER_FW_DIR o copiarlo en firmware_local/).");

        var a = FirmwareAnalyzer.AnalyzeFile(path);
        Assert.Equal(sha, a.Sha256);
        Assert.True(a.ContainerValid, string.Join("\n", a.Checks.Where(c => c.Status == CheckStatus.Error).Select(c => c.Detail)));
        Assert.Equal(flashEnd, a.Header!.FlashEnd);
        Assert.Equal(crc, a.Header.Crc);
        Assert.Equal(crc, a.ComputedCrc);
        Assert.Equal(appSize, a.AppSize);
        Assert.Equal(kind, Identifier.Identify(a, FirmwareCatalog.LoadEmbedded()).Kind);
    }

    [Fact]
    public void UnBitCambiadoEnUnOriginalSeDetecta()
    {
        var path = FindFirmware("MST_EXT_SPI_ISP_20220511_165639.BIN");
        if (path is null)
            Assert.Skip("No está el original del LG.");
        var fw = File.ReadAllBytes(path);
        fw[0x30000] ^= 0x80;
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.False(a.ContainerValid);
        Assert.Equal(IdentificationKind.DesconocidoInvalido, Identifier.Identify(a, FirmwareCatalog.LoadEmbedded()).Kind);
    }

    [Fact]
    public void CambioFueraDeLaAppConCrcIntactoEsDesconocidoValido()
    {
        // Simula un firmware "modificado por otro": cambia un byte de la base de datos (fuera del CRC).
        var path = FindFirmware("Atec_32_KPC_Salva.BIN");
        if (path is null)
            Assert.Skip("No está el original del ATEC.");
        var fw = File.ReadAllBytes(path);
        fw[0x3F0000] ^= 0x01;
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.True(a.ContainerValid);
        Assert.Equal(IdentificationKind.DesconocidoValido, Identifier.Identify(a, FirmwareCatalog.LoadEmbedded()).Kind);
    }

    [Fact]
    public void ZopfliRecomprimeLaAppDelLgV5IgualQuePython()
    {
        // La app del LG v5 la comprimió patch_ir_lg.py con zopfli (5 iteraciones).
        // Recomprimirla con el port debe dar exactamente el mismo deflate que hay en el .BIN.
        var path = FindFirmware("LG_42LF652VX_MANDO_SOYEA_v5.BIN");
        if (path is null)
            Assert.Skip("No está LG_42LF652VX_MANDO_SOYEA_v5.BIN.");
        var fw = File.ReadAllBytes(path);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.True(a.ContainerValid);
        var comp = ZopfliDeflater.Compress(a.App, new ZopfliOptions(NumIterations: 5));
        var expected = fw.AsSpan(MStarLayout.DeflateOffset, a.DeflateEnd!.Value - MStarLayout.DeflateOffset).ToArray();
        Assert.Equal(expected.Length, comp.Length);
        Assert.Equal(expected, comp);
    }

    [Theory]
    [InlineData("original", "17918f8fa6607bb802fdf6c2e71f554b547a390201eb65b9e704494b9fd9b002")]
    [InlineData("off", "3a836aee3dacb7d60fade3f7e834e8578309156e58a02e365e122fe236fed599")]
    public void PerfilLgGeneraLoMismoQuePatchIrLgV5(string espejo, string expectedSha)
    {
        var path = FindFirmware("MST_EXT_SPI_ISP_20220511_165639.BIN");
        if (path is null)
            Assert.Skip("No está el original del LG.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "lg-42lf652vx-soyea");
        var r = FirmwareBuilder.Build(File.ReadAllBytes(path), profile,
            new Dictionary<string, string> { ["mando"] = "si", ["espejo"] = espejo }, cancel: TestContext.Current.CancellationToken);
        Assert.Equal(expectedSha, r.OutputSha256);
        Assert.True(r.MatchesKnown);
    }

    [Fact]
    public void PerfilLgSoloEspejo()
    {
        var path = FindFirmware("MST_EXT_SPI_ISP_20220511_165639.BIN");
        if (path is null)
            Assert.Skip("No está el original del LG.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "lg-42lf652vx-soyea");
        var r = FirmwareBuilder.Build(File.ReadAllBytes(path), profile,
            new Dictionary<string, string> { ["mando"] = "no", ["espejo"] = "off" }, cancel: TestContext.Current.CancellationToken);
        Assert.Equal(1, r.ChangedAppBytes); // f8a10163 -> f8010163: un solo byte distinto
        Assert.Null(r.Known);
        Assert.True(FirmwareAnalyzer.Analyze(r.Output).ContainerValid);
    }

    [Theory]
    [InlineData("menu", "girado", "81bd3c8b643745d321a99f3a6479dee8dc97b464d55bbb3be006fb6160b93570", 137)]
    [InlineData("menu", "original", "3bffdfd07255108fca0d1edc5cfa154f79e27375a3a4281d3284aa0d76750e59", 137)]
    [InlineData("fijo", "original", "98a919fbef5f848147272fe37a6ccd51bbd0166c0eb22ae8a75125ef211e5fe0", 2 + 14 * 3)]
    public void PerfilKonkaGeneraLasVersionesProbadas(string modo, string logo, string expectedSha, int changedBytes)
    {
        var path = FindFirmware("TV4-32K16.bin");
        if (path is null)
            Assert.Skip("No está el original del Konka.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "konka-tv4-32k16-mirror");
        var r = FirmwareBuilder.Build(File.ReadAllBytes(path), profile,
            new Dictionary<string, string> { ["modo"] = modo, ["logo"] = logo },
            cancel: TestContext.Current.CancellationToken);
        Assert.Equal(expectedSha, r.OutputSha256);
        Assert.True(r.MatchesKnown);
        Assert.Equal(changedBytes, r.ChangedAppBytes);
    }

    [Fact]
    public void GirarElLogoDaElMismoJpegQuePillow()
    {
        // El logo de TV4-32K16_MIRROR_MENU.bin lo generó Pillow 12.3.0 / libjpeg-turbo 3.1.4.1.
        var orig = FindFirmware("TV4-32K16.bin");
        var pillow = FindFirmware("TV4-32K16_MIRROR_MENU.bin");
        if (orig is null || pillow is null)
            Assert.Skip("Faltan los archivos del Konka.");
        const int Off = 0x29B1B8, Len = 131381;
        var jpeg = File.ReadAllBytes(orig).AsSpan(Off, Len).ToArray();
        var expectedSlot = File.ReadAllBytes(pillow).AsSpan(Off, Len).ToArray();
        var rotated = FirmReaper.Core.Imaging.LogoImage.Rotated(jpeg, Len).Jpeg;
        Assert.True(expectedSlot.AsSpan(0, rotated.Length).SequenceEqual(rotated));
        Assert.True(expectedSlot.AsSpan(rotated.Length).IndexOfAnyExcept((byte)0) < 0);
    }

    /// <summary>
    /// Girar el logo del ATEC (4:2:0, reinicio cada 136 MCU) y del LG (4:2:2, sin reinicios) da el MISMO JPEG
    /// que Pillow 12.3.0 con: im.convert("RGB").rotate(180).save(…, qtables=im.quantization,
    /// subsampling=2|1, restart_marker_blocks=136|—). Huellas calculadas con ese código (docs/PRUEBAS.md).
    /// </summary>
    [Theory]
    [InlineData("Atec_32_KPC_Salva.BIN", 0x2D5010, 49128, "d22a2ea3be43c05b5d120707d37e952455cc27f033b55711e4e99589cbcd74ef")]
    [InlineData("MST_EXT_SPI_ISP_20220511_165639.BIN", 0x370000, 27753, "d0f6f59f3132d8f72f6740b2efa217aa94b0a17719b431f1c58946a4684250ed")]
    public void GirarLogos420y422IgualQuePillow(string file, int off, int len, string expectedSha)
    {
        var path = FindFirmware(file);
        if (path is null)
            Assert.Skip($"No está {file}.");
        var jpeg = File.ReadAllBytes(path).AsSpan(off, len).ToArray();
        var rotated = FirmReaper.Core.Imaging.LogoImage.Rotated(jpeg, 1 << 20).Jpeg;
        Assert.Equal(expectedSha, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rotated)));
    }

    [Theory]
    [InlineData("girado")]
    [InlineData("propio")]
    public void AtecLogoSoloCambiaElRecursoYMantieneSuCrc(string logo)
    {
        var path = FindFirmware("Atec_32_KPC_Salva.BIN");
        if (path is null)
            Assert.Skip("No está el original del ATEC.");
        var fw = File.ReadAllBytes(path);
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "atec-32l18d-lp141wx1");
        var custom = logo == "propio" ? LogoTests.Bmp24(new FirmReaper.Core.Imaging.RgbImage(
            Enumerable.Range(0, 640 * 360 * 3).Select(i => (byte)(i % 3 == 0 ? 255 : i / 3 % 640 * 255 / 640)).ToArray(), 640, 360)) : null;
        var r = FirmwareBuilder.Build(fw, profile, new Dictionary<string, string> { ["logo"] = logo }, custom,
            cancel: TestContext.Current.CancellationToken);

        // Con bits=8 por defecto la app cambia (entrada del panel); fuera de la app solo cambia el recurso 0x0300.
        const int ResOff = 0x2D5010, ResLen = 49136;
        var o = r.Output;
        Assert.True(o.AsSpan(0x2D5010 - 0x100, 0x100).SequenceEqual(fw.AsSpan(0x2D5010 - 0x100, 0x100)));
        Assert.True(o.AsSpan(ResOff + ResLen).SequenceEqual(fw.AsSpan(ResOff + ResLen)));
        Assert.True(Crc16Tel.IsValid(o.AsSpan(ResOff, ResLen)));
        Assert.False(o.AsSpan(ResOff, ResLen).SequenceEqual(fw.AsSpan(ResOff, ResLen)));
        var a = FirmReaper.Core.Analysis.FirmwareAnalyzer.Analyze(o);
        Assert.True(a.ContainerValid);
        Assert.Equal(FirmReaper.Core.Analysis.CheckStatus.Ok, a.Checks.Single(c => c.Id == "tel.crc").Status);
        var info = FirmReaper.Core.Imaging.JpegInfo.Parse(o.AsSpan(ResOff, ResLen - 2).ToArray());
        Assert.Equal((2167, 1228, (2, 2), 136), (info.Width, info.Height, info.EncoderSampling!.Value, info.RestartInterval));
    }

    [Fact]
    public void LgLogoSoloCambiaLaZonaDelLogoCapturado()
    {
        var path = FindFirmware("MST_EXT_SPI_ISP_20220511_165639.BIN");
        if (path is null)
            Assert.Skip("No está el original del LG.");
        var fw = File.ReadAllBytes(path);
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "lg-42lf652vx-soyea");
        // Solo el logo: sin mando ni espejo → la app no cambia y no se recomprime.
        var r = FirmwareBuilder.Build(fw, profile,
            new Dictionary<string, string> { ["mando"] = "no", ["espejo"] = "original", ["logo"] = "girado" },
            cancel: TestContext.Current.CancellationToken);
        var o = r.Output;
        Assert.True(o.AsSpan(0, 0x370000).SequenceEqual(fw.AsSpan(0, 0x370000)));
        Assert.True(o.AsSpan(0x3B0000).SequenceEqual(fw.AsSpan(0x3B0000)));
        Assert.Equal(27727, r.LogoLength);
        Assert.True(o.AsSpan(0x370000 + r.LogoLength, 0x40000 - r.LogoLength).IndexOfAnyExcept((byte)0xFF) < 0);
        Assert.Equal("d0f6f59f3132d8f72f6740b2efa217aa94b0a17719b431f1c58946a4684250ed", r.LogoSha256);
    }

    [Fact]
    public void LogoPropioDemasiadoGrandeSeRecomprimeParaCaber()
    {
        var path = FindFirmware("TV4-32K16.bin");
        if (path is null)
            Assert.Skip("No está el original del Konka.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "konka-tv4-32k16-mirror");
        // JPEG válido 1366x768 pero de más de 131 381 bytes (ruido con calidad máxima).
        var big = LogoTests.NoiseJpeg(1366, 768, quality1: true);
        Assert.True(big.Length > 131381);
        var r = FirmwareBuilder.Build(File.ReadAllBytes(path), profile,
            new Dictionary<string, string> { ["logo"] = "propio" }, big, cancel: TestContext.Current.CancellationToken);
        Assert.True(r.LogoLength <= 131381);
        Assert.Contains("calidad reducida", r.LogoDetail);
    }

    [Fact]
    public void LgConTablaEditadaSoloCambiaLaTabla()
    {
        var path = FindFirmware("MST_EXT_SPI_ISP_20220511_165639.BIN");
        if (path is null)
            Assert.Skip("No está el original del LG.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "lg-42lf652vx-soyea");
        var kt = profile.KeyTable!;
        // Ejemplo: el botón SLEEP (0x45) pasa a ser INPUT_SOURCE (0x22).
        var map = kt.Default.Select(k => k.Code == 0x45 ? k with { Index = 0x22 } : k).ToList();
        var fw = File.ReadAllBytes(path);
        var r = FirmwareBuilder.Build(fw, profile, new Dictionary<string, string> { ["mando"] = "si", ["espejo"] = "original" },
            cancel: TestContext.Current.CancellationToken, keyMap: map);
        Assert.True(r.KeyMapEdited);
        Assert.Null(r.Known); // ya no es la versión documentada
        var a = FirmwareAnalyzer.Analyze(r.Output);
        Assert.True(a.ContainerValid);
        // La tabla escrita en la app es la editada.
        Assert.Equal(map, kt.Decode(a.App!.AsSpan((int)(kt.Address - profile.AppBaseRam), kt.ByteLength)));
        // Y respecto a la v5 documentada solo cambia 1 byte (el índice de SLEEP).
        var v5 = FirmwareBuilder.Build(fw, profile, new Dictionary<string, string> { ["mando"] = "si", ["espejo"] = "original" },
            cancel: TestContext.Current.CancellationToken);
        var b = FirmwareAnalyzer.Analyze(v5.Output);
        Assert.Equal(1, a.App!.Zip(b.App!).Count(t => t.First != t.Second));
    }

    [Theory]
    [InlineData("Atec32L18D_LP141WX1_8bit.BIN")]
    [InlineData("Atec32L18D_LP141WX1_6bit.BIN")]
    public void ZlibRecomprimeLaAppDelAtecIgualQuePython(string file)
    {
        // patch_panel.py comprimió con zlib -9 (zlib 1.3).
        var path = FindFirmware(file);
        if (path is null)
            Assert.Skip($"No está {file}.");
        var fw = File.ReadAllBytes(path);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.True(a.ContainerValid);
        var comp = FirmReaper.Core.Compression.ZlibDeflater.Compress(a.App, 9);
        var expected = fw.AsSpan(MStarLayout.DeflateOffset, a.DeflateEnd!.Value - MStarLayout.DeflateOffset).ToArray();
        Assert.Equal(expected.Length, comp.Length);
        Assert.Equal(expected, comp);
    }

    [Theory]
    [InlineData("8", "ab3d3485707e3c271e129ae9be633ad6ca3d63aa2051c5ef16596dbc727c918c", 25)]
    [InlineData("6", "7bd55c392efabf11ad37b61eff631792ba285bb82a1ce71d06bdee89976d65e2", 28)]
    public void PerfilAtecGeneraLoMismoQuePatchPanel(string bits, string expectedSha, int changedBytes)
    {
        var path = FindFirmware("Atec_32_KPC_Salva.BIN");
        if (path is null)
            Assert.Skip("No está el original del ATEC.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "atec-32l18d-lp141wx1");
        var r = FirmwareBuilder.Build(File.ReadAllBytes(path), profile,
            new Dictionary<string, string> { ["resolucion"] = "1280x800", ["bits"] = bits },
            cancel: TestContext.Current.CancellationToken);
        Assert.Equal(expectedSha, r.OutputSha256);
        Assert.True(r.MatchesKnown);
        Assert.Equal(changedBytes, r.ChangedAppBytes);
    }

    [Fact]
    public void AtecConMandoSoyeaEsElArchivoProbadoQueEnciendeDesdeReposo()
    {
        var path = FindFirmware("Atec_32_KPC_Salva.BIN");
        if (path is null)
            Assert.Skip("No está el original del ATEC.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "atec-32l18d-lp141wx1");
        var fw = File.ReadAllBytes(path);
        var r = FirmwareBuilder.Build(fw, profile,
            new Dictionary<string, string> { ["resolucion"] = "1280x800", ["bits"] = "8", ["mando"] = "soyea" },
            cancel: TestContext.Current.CancellationToken);
        // = ATEC_8bit_mando_Soyea_PRUEBA_reposo.BIN, probado en el TV (2026-11-22): mando, apagar y encender.
        Assert.Equal("8e797d5dbb8056ec36630b3653eb194a35e08a515e86d404f6a4706ef92aaecd", r.OutputSha256);
        Assert.True(r.MatchesKnown);
        // panel (25) + sfeqi (3) + inicialización IR (2) + entrada en reposo, código de arranque sin comprimir (2)
        Assert.Equal(25 + 3 + 2 + 2, r.ChangedAppBytes);
        var a = FirmwareAnalyzer.Analyze(r.Output);
        Assert.True(a.App!.AsSpan(0x3233D5 - 0x243800, 4).SequenceEqual(Convert.FromHexString("c0ef06e0")));
        Assert.True(a.App.AsSpan(0x32394C - 0x243800, 3).SequenceEqual(Convert.FromHexString("508037")));
        Assert.True(a.App.AsSpan(0x323957 - 0x243800, 3).SequenceEqual(Convert.FromHexString("508078")));
        Assert.Equal("50e037", Convert.ToHexStringLower(r.Output.AsSpan(0x23195, 3)));   // RAM 0x243115
        Assert.Equal("50e078", Convert.ToHexStringLower(r.Output.AsSpan(0x231A0, 3)));   // RAM 0x243120
        // Las mismas instrucciones que el firmware del Soyea.
        var soyea = FindFirmware("MST_EXT_SPI_ISP_20230301_153737.BIN");
        if (soyea is not null)
        {
            var sf = File.ReadAllBytes(soyea);
            var sa = FirmwareAnalyzer.Analyze(sf).App!;
            Assert.True(sa.AsSpan(0x3257A1 - 0x243800, 3).SequenceEqual(Convert.FromHexString("508037")));
            Assert.True(sa.AsSpan(0x3257AC - 0x243800, 3).SequenceEqual(Convert.FromHexString("508078")));
            Assert.Equal("50e037", Convert.ToHexStringLower(sf.AsSpan(0x23195, 3)));
            Assert.Equal("50e078", Convert.ToHexStringLower(sf.AsSpan(0x231A0, 3)));
        }
    }

    [Theory]
    [InlineData("1280x800", 1280, 800, 1440, 823, 71)]
    [InlineData("1280x720", 1280, 720, 1650, 750, 74)]
    [InlineData("1024x768", 1024, 768, 1344, 806, 65)]
    [InlineData("800x600", 800, 600, 1056, 628, 40)]
    public void AtecResolucionesEscribenLosTiemposDelEstandar(string res, int w, int h, int ht, int vt, int dclk)
    {
        if (Load("Atec_32_KPC_Salva.BIN", "atec-32l18d-lp141wx1") is not var (fw, p)) { Assert.Skip("No está el original del ATEC."); return; }
        var r = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["resolucion"] = res },
            cancel: TestContext.Current.CancellationToken);
        var e = FirmwareAnalyzer.Analyze(r.Output).App!.AsSpan(0x4E3A78 - 0x243800, 92).ToArray();
        int U16(int k) => e[k] | (e[k + 1] << 8);
        Assert.Equal((w, h), (U16(28), U16(30)));
        Assert.Equal((ht, vt, dclk), (U16(34), U16(40), (int)e[45]));
        Assert.Equal(U16(24), e[20] + e[21]);                       // HStart = HSync + HBackPorch
        Assert.Equal(U16(26), e[22] + e[23]);                       // VStart = VSync + VBackPorch
        Assert.True(U16(36) <= ht && ht <= U16(32) && U16(42) <= vt && vt <= U16(38) && e[46] <= dclk && dclk <= e[44]);
        if (res != "1280x800") // márgenes calculados (el LP141WX1 usa los de su hoja de datos)
            Assert.True(U16(36) >= w + e[20] + e[21] + 16);         // porche delantero ≥ 16
        Assert.Equal(2, e[64]);                                     // 8 bits por defecto
        // 1280x800 a 8 bits es la versión probada (Atec32L18D_LP141WX1_8bit.BIN); las demás no tienen versión conocida.
        Assert.Equal(res == "1280x800", r.MatchesKnown);
    }

    // ------------------------------------------------------------------ mando genérico (etapa 8)

    private static (byte[] Fw, Profile P)? Load(string file, string profileId)
    {
        var path = FindFirmware(file);
        return path is null ? null : (File.ReadAllBytes(path), ProfileLoader.LoadBuiltin().Single(p => p.Id == profileId));
    }

    [Fact]
    public void AtecConOtroCodigoDeClienteYTablaEditada()
    {
        if (Load("Atec_32_KPC_Salva.BIN", "atec-32l18d-lp141wx1") is not var (fw, p)) { Assert.Skip("No está el original del ATEC."); return; }
        var kt = p.KeyTable!;
        // SLEEP (botón 0x45) → INFO (0x22), y cliente 12 34.
        var map = kt.Default.Select(k => k.Code == 0x45 ? k with { Index = 0x22 } : k).ToList();
        var r = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["resolucion"] = "1280x800", ["mando"] = "soyea" },
            cancel: TestContext.Current.CancellationToken, keyMap: map, clientCode: new NecCustomer(0x12, 0x34));
        Assert.True(r.KeyMapEdited && r.ClientCodeEdited);
        Assert.Equal(new NecCustomer(0x12, 0x34), r.ClientCode);
        Assert.Null(r.Known);
        var app = FirmwareAnalyzer.Analyze(r.Output).App!;
        Assert.Equal("c0e68240", Convert.ToHexStringLower(app.AsSpan(0x3233D5 - 0x243800, 4))); // sfeqi r7,0x3412
        Assert.Equal("508012", Convert.ToHexStringLower(app.AsSpan(0x32394C - 0x243800, 3)));   // reposo: 1.er byte
        Assert.Equal("508034", Convert.ToHexStringLower(app.AsSpan(0x323957 - 0x243800, 3)));   // reposo: 2.º byte
        Assert.Equal(0x22, app[0x422090 - 0x243800 + 0x45]);
        Assert.Equal("50e012", Convert.ToHexStringLower(r.Output.AsSpan(0x23195, 3)));  // entrada en reposo: 1.er byte
        Assert.Equal("50e034", Convert.ToHexStringLower(r.Output.AsSpan(0x231A0, 3)));  // entrada en reposo: 2.º byte
        // Respecto al original: panel (25) + 3 bytes del inmediato + 2 de inicialización IR + 2 de entrada en reposo + 1 de la tabla.
        Assert.Equal(25 + 3 + 2 + 2 + 1, r.ChangedAppBytes);
        // Un código con el 2.º byte > 7F no se puede poner en el ATEC.
        Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p, new Dictionary<string, string>(),
            cancel: TestContext.Current.CancellationToken, clientCode: new NecCustomer(0x00, 0xBF)));
    }

    [Fact]
    public void KonkaConClienteSoyeaYTablaEditada()
    {
        if (Load("TV4-32K16.bin", "konka-tv4-32k16-mirror") is not var (fw, p)) { Assert.Skip("No está el original del Konka."); return; }
        var kt = p.KeyTable!;
        // Quitar el último botón y asignar un código nuevo (0x70) a POWER.
        var map = kt.Default.Take(44).Append(new KeyAssignment(0x70, 0x01)).ToList();
        var r = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["logo"] = "original" },
            cancel: TestContext.Current.CancellationToken, keyMap: map, clientCode: new NecCustomer(0x37, 0x78));
        var app = FirmwareAnalyzer.Analyze(r.Output).App!;
        int o = (int)(0x38FFA2 - p.AppBaseRam);
        Assert.Equal("7837", Convert.ToHexStringLower(app.AsSpan(o, 2)));           // invertido en memoria
        Assert.Equal(map, kt.Decode(app.AsSpan(o + 2, kt.ByteLength)));
        Assert.Equal("70010000", Convert.ToHexStringLower(app.AsSpan(o + 2 + 44 * 4, 4)));
        Assert.Equal("ffff0000", Convert.ToHexStringLower(app.AsSpan(o + 2 + 45 * 4, 4)));
        // Lo que sigue a la tabla no cambia.
        var orig = FirmwareAnalyzer.Analyze(fw).App!;
        int end = o + 2 + kt.ByteLength;
        Assert.True(app.AsSpan(end, 4096).SequenceEqual(orig.AsSpan(end, 4096)));
    }

    [Fact]
    public void LgConOtroCodigoDeClienteSinMandoSoyea()
    {
        if (Load("MST_EXT_SPI_ISP_20220511_165639.BIN", "lg-42lf652vx-soyea") is not var (fw, p)) { Assert.Skip("No está el original del LG."); return; }
        var r = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["mando"] = "no", ["espejo"] = "off" },
            cancel: TestContext.Current.CancellationToken, clientCode: new NecCustomer(0x12, 0x34));
        var app = FirmwareAnalyzer.Analyze(r.Output).App!;
        Assert.Equal("3412", Convert.ToHexStringLower(app.AsSpan((int)(0x39DEFE - p.AppBaseRam), 2)));
        Assert.Equal(1 + 2, r.ChangedAppBytes); // espejo (1) + cliente (2)
        // Poner el mismo código que da la opción no cambia nada: sale la v5 documentada.
        var v5 = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["mando"] = "si", ["espejo"] = "original" },
            cancel: TestContext.Current.CancellationToken, clientCode: new NecCustomer(0x37, 0x78));
        Assert.True(v5.MatchesKnown);
        Assert.False(v5.ClientCodeEdited);
    }

    [Fact]
    public void LaTablaDelMandoDelAtecEsLaDelSoyea()
    {
        // Base de la opción "mando=soyea": misma tabla código→función (256 bytes) en ambos firmwares.
        var atec = FindFirmware("Atec_32_KPC_Salva.BIN");
        var soyea = FindFirmware("MST_EXT_SPI_ISP_20230301_153737.BIN");
        if (atec is null || soyea is null)
            Assert.Skip("Faltan los firmwares del ATEC o del Soyea.");
        var a = FirmwareAnalyzer.AnalyzeFile(atec).App!;
        var s = FirmwareAnalyzer.AnalyzeFile(soyea).App!;
        Assert.True(a.AsSpan(0x422090 - 0x243800, 256).SequenceEqual(s.AsSpan(0x423CE0 - 0x243800, 256)));
        // Y la comparación del cliente del Soyea es exactamente la instrucción que escribe el parche.
        Assert.True(s.AsSpan(0x3251E3 - 0x243800, 4).SequenceEqual(Convert.FromHexString("c0ef06e0")));
    }

    // ------------------------------------------------------------------ placa LM2SA V1.6 (1.4.0)

    [Theory]
    [InlineData("MST_EXT_SPI_ISP_20210531_113958.BIN", "lm2sa-v16-20210531", 0x4D106C, 0x31EAC1)]
    [InlineData("MST_EXT_SPI_ISP_20210614_165953.BIN", "lm2sa-v16-20210614", 0x4BB06C, 0x31E049)]
    public void Lm2saCambiaLoMismoQueElAtecProbado(string file, string id, int panel, int ir)
    {
        if (Load(file, id) is not var (fw, p)) { Assert.Skip("No está el original de la placa LM2SA."); return; }
        var atec = ProfileLoader.LoadBuiltin().Single(x => x.Id == "atec-32l18d-lp141wx1");
        var r = FirmwareBuilder.Build(fw, p,
            new Dictionary<string, string> { ["resolucion"] = "1280x800", ["bits"] = "8", ["mando"] = "soyea" },
            cancel: TestContext.Current.CancellationToken);
        // Los mismos 32 bytes que la versión del ATEC probada en el TV (panel 25 + cliente 3 + reposo 2 + arranque 2).
        Assert.Equal(25 + 3 + 2 + 2, r.ChangedAppBytes);
        var app = FirmwareAnalyzer.Analyze(r.Output).App!;
        var e = app.AsSpan(panel - 0x243800, 92).ToArray();
        var ea = atec.Options.Single(o => o.Id == "resolucion").Values.Single(v => v.Id == "1280x800").Patches;
        Assert.Equal(Convert.ToHexStringLower(ea[0].New), Convert.ToHexStringLower(e.AsSpan(20, 27)));
        Assert.Equal(Convert.ToHexStringLower(ea[1].New), Convert.ToHexStringLower(e.AsSpan(76, 8)));
        Assert.Equal("c0ef06e0", Convert.ToHexStringLower(app.AsSpan(ir - 0x243800, 4)));
        Assert.Equal("508037", Convert.ToHexStringLower(app.AsSpan(ir + 0x577 - 0x243800, 3)));
        Assert.Equal("508078", Convert.ToHexStringLower(app.AsSpan(ir + 0x582 - 0x243800, 3)));
        Assert.Equal("50e037", Convert.ToHexStringLower(r.Output.AsSpan(0x23195, 3)));
        Assert.Equal("50e078", Convert.ToHexStringLower(r.Output.AsSpan(0x231A0, 3)));
        // Recursos y base de datos sin tocar.
        var first = (int)FirmwareAnalyzer.Analyze(fw).Tel!.Entries.Min(t => t.Offset);
        Assert.True(r.Output.AsSpan(first).SequenceEqual(fw.AsSpan(first)));
    }

    [Theory]
    [InlineData("MST_EXT_SPI_ISP_20210531_113958.BIN", "lm2sa-v16-20210531", 0x2C86A0)]
    [InlineData("MST_EXT_SPI_ISP_20210614_165953.BIN", "lm2sa-v16-20210614", 0x2C0440)]
    public void Lm2saLogoGiradoSoloCambiaElRecurso(string file, string id, int resOff)
    {
        if (Load(file, id) is not var (fw, p)) { Assert.Skip("No está el original de la placa LM2SA."); return; }
        const int ResLen = 55088;
        var r = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["logo"] = "girado" },
            cancel: TestContext.Current.CancellationToken);
        var o = r.Output;
        Assert.True(o.AsSpan(0, resOff).SequenceEqual(fw.AsSpan(0, resOff)));          // la app no cambia
        Assert.True(o.AsSpan(resOff + ResLen).SequenceEqual(fw.AsSpan(resOff + ResLen)));
        Assert.True(Crc16Tel.IsValid(o.AsSpan(resOff, ResLen)));
        var info = FirmReaper.Core.Imaging.JpegInfo.Parse(o.AsSpan(resOff, ResLen - 2).ToArray());
        Assert.Equal((720, 480), (info.Width, info.Height));
    }
}

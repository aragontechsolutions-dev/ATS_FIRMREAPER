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
        var rotated = FirmReaper.Core.Imaging.LogoImage.Rotate180(jpeg);
        Assert.True(expectedSlot.AsSpan(0, rotated.Length).SequenceEqual(rotated));
        Assert.True(expectedSlot.AsSpan(rotated.Length).IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void LogoPropioDemasiadoGrandeSeRechaza()
    {
        var path = FindFirmware("TV4-32K16.bin");
        if (path is null)
            Assert.Skip("No está el original del Konka.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "konka-tv4-32k16-mirror");
        // JPEG válido 1366x768 pero de más de 131 381 bytes (ruido con calidad máxima).
        var big = LogoTests.NoiseJpeg(1366, 768, quality1: true);
        Assert.True(big.Length > 131381);
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.Build(File.ReadAllBytes(path), profile,
            new Dictionary<string, string> { ["logo"] = "propio" }, big, cancel: TestContext.Current.CancellationToken));
        Assert.Contains("máximo", ex.Message);
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
        var r = FirmwareBuilder.Build(File.ReadAllBytes(path), profile, new Dictionary<string, string> { ["bits"] = bits },
            cancel: TestContext.Current.CancellationToken);
        Assert.Equal(expectedSha, r.OutputSha256);
        Assert.True(r.MatchesKnown);
        Assert.Equal(changedBytes, r.ChangedAppBytes);
    }

    [Fact]
    public void AtecConMandoSoyeaSoloCambiaLaComparacionDelCliente()
    {
        var path = FindFirmware("Atec_32_KPC_Salva.BIN");
        if (path is null)
            Assert.Skip("No está el original del ATEC.");
        var profile = ProfileLoader.LoadBuiltin().Single(p => p.Id == "atec-32l18d-lp141wx1");
        var fw = File.ReadAllBytes(path);
        var r = FirmwareBuilder.Build(fw, profile, new Dictionary<string, string> { ["bits"] = "8", ["mando"] = "soyea" },
            cancel: TestContext.Current.CancellationToken);
        Assert.Null(r.Known);
        Assert.Equal(25 + 3, r.ChangedAppBytes); // panel (25) + inmediato de sfeqi (3)
        var a = FirmwareAnalyzer.Analyze(r.Output);
        Assert.True(a.App.AsSpan(0x3233D5 - 0x243800, 4).SequenceEqual(Convert.FromHexString("c0ef06e0")));
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
}

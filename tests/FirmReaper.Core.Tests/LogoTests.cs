using System.Text.Json.Nodes;
using FirmReaper.Core.Imaging;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Tests;

public class LogoTests
{
    private static readonly LogoSpec Spec = new(0x065E, 131381, 1366, 768);

    /// <summary>JPEG baseline 4:4:4 generado con el codificador propio.</summary>
    internal static byte[] NoiseJpeg(int w, int h, bool quality1 = false, int seed = 1)
    {
        var rnd = new Random(seed);
        var rgb = new byte[w * h * 3];
        if (quality1) rnd.NextBytes(rgb);
        else
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)((i / 3 % w) * 255 / w);
        var q = Enumerable.Repeat(quality1 ? 1 : 16, 64).ToArray();
        return JpegBaselineEncoder.Encode(rgb, w, h, q, q, 0);
    }

    [Fact]
    public void ElCodificadorProduceUnJpegQueSeDecodifica()
    {
        var j = NoiseJpeg(1366, 768);
        var info = JpegInfo.Parse(j);
        Assert.Equal((1366, 768), (info.Width, info.Height));
        Assert.True(info.IsBaseline && info.Is444 && info.Length == j.Length);
        var rgb = LogoImage.DecodeRgb(j, out int w, out int h);
        Assert.Equal(1366 * 768 * 3, rgb.Length);
        FirmwareBuilder.ValidateLogo(j, Spec); // no lanza
        Assert.Equal((1366, 768), (w, h));
    }

    [Fact]
    public void GirarDosVecesConservaLasDimensionesYLasTablas()
    {
        var j = NoiseJpeg(1366, 768);
        var r = LogoImage.Rotate180(j);
        var a = JpegInfo.Parse(j);
        var b = JpegInfo.Parse(r);
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        Assert.Equal(a.QuantTables[0], b.QuantTables[0]);
        Assert.Equal(a.QuantTables[1], b.QuantTables[1]);
    }

    [Fact]
    public void RechazaDimensionesDistintas()
    {
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(NoiseJpeg(800, 600), Spec));
        Assert.Contains("1366x768", ex.Message);
    }

    [Fact]
    public void RechazaProgresivo()
    {
        var j = NoiseJpeg(1366, 768);
        int sof = j.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        j[sof + 1] = 0xC2;
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(j, Spec));
        Assert.Contains("baseline", ex.Message);
    }

    [Fact]
    public void RechazaDatosTrasElFinal()
    {
        var j = NoiseJpeg(1366, 768).Concat(new byte[] { 1, 2, 3 }).ToArray();
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(j, Spec));
        Assert.Contains("después", ex.Message);
    }

    [Fact]
    public void RechazaJpegTruncadoONoJpeg()
    {
        var j = NoiseJpeg(1366, 768);
        Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(j.AsSpan(0, j.Length / 2).ToArray(), Spec));
        Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo("no es un jpeg"u8.ToArray(), Spec));
    }

    [Fact]
    public void ElParserNoRevientaConBasura()
    {
        var rnd = new Random(3);
        var good = NoiseJpeg(64, 48);
        for (int n = 0; n < 3000; n++)
        {
            var j = (byte[])good.Clone();
            for (int k = 0; k < 4; k++) j[rnd.Next(j.Length)] = (byte)rnd.Next(256);
            try
            {
                JpegInfo.Parse(j);
            }
            catch (InvalidDataException)
            {
                // esperado
            }
        }
    }

    [Theory]
    [InlineData("accion desconocida")]
    [InlineData("logo sin recurso")]
    [InlineData("dos opciones de logo")]
    [InlineData("dimensiones absurdas")]
    public void PerfilConLogoMalDefinidoSeRechaza(string caso)
    {
        var j = ProfileTests.Minimal();
        var valores = j["opciones"]![0]!["valores"]!;
        j["logo"] = JsonNode.Parse("""{"idTel":"0x065E","longitudJpeg":131381,"ancho":1366,"alto":768}""");
        switch (caso)
        {
            case "accion desconocida": valores[0]!["logo"] = "borrar"; break;
            case "logo sin recurso": valores[0]!["logo"] = "girar"; j.Remove("logo"); break;
            case "dos opciones de logo":
                valores[0]!["logo"] = "girar";
                j["opciones"]!.AsArray().Add(JsonNode.Parse("""{"id":"op2","etiqueta":"x","tipo":"lista","porDefecto":"a","valores":[{"id":"a","etiqueta":"A","parches":[],"logo":"propio"},{"id":"b","etiqueta":"B","parches":[]}]}"""));
                break;
            case "dimensiones absurdas": j["logo"]!["ancho"] = 0; break;
        }
        Assert.Throws<InvalidDataException>(() => ProfileTests.Parse(j));
    }

    [Fact]
    public void PerfilConLogoValido()
    {
        var j = ProfileTests.Minimal();
        j["logo"] = JsonNode.Parse("""{"idTel":"0x065E","longitudJpeg":131381,"ancho":1366,"alto":768}""");
        j["opciones"]![0]!["valores"]![1]!["logo"] = "girar";
        var p = ProfileTests.Parse(j);
        Assert.Equal(LogoAction.Ninguna, p.LogoActionFor(new Dictionary<string, string> { ["op"] = "a" }));
        Assert.Equal(LogoAction.Girar, p.LogoActionFor(new Dictionary<string, string> { ["op"] = "b" }));
    }
}

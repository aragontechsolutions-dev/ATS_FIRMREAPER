using System.Text;
using FirmReaper.Core.Catalog;

namespace FirmReaper.Core.Tests;

public class CatalogTests
{
    private const string ShaA = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string ShaB = "2222222222222222222222222222222222222222222222222222222222222222";

    private static FirmwareCatalog P(string json) => FirmwareCatalog.Parse(Encoding.UTF8.GetBytes(json));

    private static string Img(string sha, string tipo, string? origen = null, string extra = "") =>
        $$"""{"sha256":"{{sha}}","tipo":"{{tipo}}","modelo":"M","placa":"P","nombre":"N"{{(origen is null ? "" : $",\"origen\":\"{origen}\"")}}{{extra}}}""";

    private static string Cat(params string[] imgs) => $$"""{"version":1,"imagenes":[{{string.Join(",", imgs)}}]}""";

    [Fact]
    public void ElCatalogoIncrustadoEsValido()
    {
        var c = FirmwareCatalog.LoadEmbedded();
        Assert.NotEmpty(c.Images);
        Assert.Contains(c.Images, i => i.Tipo == ImageKind.Original && i.Modelo.StartsWith("LG", StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogoMinimoValido()
    {
        var c = P(Cat(Img(ShaA, "original"), Img(ShaB, "generado", ShaA, ",\"estadoPrueba\":\"no-funciono\"")));
        Assert.Equal(TestState.NoFunciono, c.Find(ShaB)!.EstadoPrueba);
        Assert.Equal(ShaA, c.Find(ShaB)!.Origen);
        Assert.NotNull(c.Find(ShaA.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("campo desconocido")]
    [InlineData("sha mal formada")]
    [InlineData("sha repetida")]
    [InlineData("generado sin origen")]
    [InlineData("original con origen")]
    [InlineData("origen inexistente")]
    [InlineData("propiedad duplicada")]
    [InlineData("version")]
    [InlineData("tipo numerico")]
    [InlineData("falta campo obligatorio")]
    public void RechazaCatalogosInvalidos(string caso)
    {
        string json = caso switch
        {
            "campo desconocido" => Cat(Img(ShaA, "original", extra: ",\"escribirEn\":\"0x0\"")),
            "sha mal formada" => Cat(Img("XYZ", "original")),
            "sha repetida" => Cat(Img(ShaA, "original"), Img(ShaA, "original")),
            "generado sin origen" => Cat(Img(ShaA, "generado")),
            "original con origen" => Cat(Img(ShaA, "original", ShaB), Img(ShaB, "original")),
            "origen inexistente" => Cat(Img(ShaA, "original"), Img(ShaB, "generado", "3333333333333333333333333333333333333333333333333333333333333333")),
            "propiedad duplicada" => Cat(Img(ShaA, "original", extra: ",\"nombre\":\"otro\"")),
            "version" => """{"version":2,"imagenes":[]}""",
            "tipo numerico" => Cat(Img(ShaA, "0")).Replace("\"tipo\":\"0\"", "\"tipo\":0", StringComparison.Ordinal),
            "falta campo obligatorio" => $$"""{"version":1,"imagenes":[{"sha256":"{{ShaA}}","tipo":"original","modelo":"M","placa":"P"}]}""",
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };
        Assert.Throws<InvalidDataException>(() => P(json));
    }
}

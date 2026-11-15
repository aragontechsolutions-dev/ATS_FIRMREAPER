using System.Text;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Tests;

public class KeyTableTests
{
    private static Profile Lg() => ProfileLoader.LoadBuiltin().Single(p => p.Id == "lg-42lf652vx-soyea");

    [Fact]
    public void LaTablaPorDefectoEsLaV5()
    {
        var kt = Lg().KeyTable!;
        Assert.Equal(44, kt.Default.Count);
        Assert.Equal(44, kt.MaxAssignments);
        Assert.Contains(new KeyAssignment(0x0C, 0x01), kt.Default); // POWER
        Assert.Contains(new KeyAssignment(0x15, 0x12), kt.Default); // VOL+
        Assert.Equal(48, kt.Buttons.Count);
        // Codificar la tabla por defecto da exactamente el parche del perfil (= patch_ir_lg.py).
        var patch = Lg().Options.Single(o => o.Id == "mando").Get("si").Patches.Single(p => p.Address == 0x39DD94);
        Assert.Equal(patch.New, kt.Encode(kt.Default));
    }

    [Fact]
    public void ExportarEImportarDaLaMismaTabla()
    {
        var p = Lg();
        var kt = p.KeyTable!;
        var json = kt.ToJson(p.Id, kt.Default);
        Assert.Equal(kt.Default, kt.FromJson(Encoding.UTF8.GetBytes(json), p.Id));
    }

    [Theory]
    [InlineData("duplicado")]
    [InlineData("funcion no permitida")]
    [InlineData("boton desconocido")]
    [InlineData("demasiadas")]
    [InlineData("vacia")]
    public void RechazaTablasInvalidas(string caso)
    {
        var kt = Lg().KeyTable!;
        var map = kt.Default.ToList();
        switch (caso)
        {
            case "duplicado": map.Add(map[0]); map.RemoveAt(1); break;
            case "funcion no permitida": map[0] = map[0] with { Index = 0x99 }; break;
            case "boton desconocido": map[0] = map[0] with { Code = 0xEE }; break;
            case "demasiadas": map.Add(new KeyAssignment(0x25, 0x01)); break; // 45 > 44
            case "vacia": map.Clear(); break;
        }
        Assert.NotEmpty(kt.Validate(map));
        Assert.Throws<ArgumentException>(() => kt.Encode(map));
    }

    [Fact]
    public void RechazaArchivoDeOtroPerfilOMalFormado()
    {
        var p = Lg();
        var kt = p.KeyTable!;
        var json = kt.ToJson(p.Id, kt.Default);
        Assert.Throws<InvalidDataException>(() => kt.FromJson(Encoding.UTF8.GetBytes(json), "otro-perfil"));
        Assert.Throws<InvalidDataException>(() => kt.FromJson(Encoding.UTF8.GetBytes(json.Replace("\"formato\": 1", "\"formato\": 1, \"x\": 2")), p.Id));
        Assert.Throws<InvalidDataException>(() => kt.FromJson("{"u8.ToArray(), p.Id));
        Assert.Throws<InvalidDataException>(() => kt.FromJson(Encoding.UTF8.GetBytes(json.Replace("\"0x12\"", "\"0x1G2\"")), p.Id));
    }

    [Fact]
    public void PerfilConTablaQueNoCorrespondeAUnParcheSeRechaza()
    {
        var j = ProfileTests.Minimal();
        j["tablaMando"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {"direccion":"0x250000","entradas":2,"prefijoCodigo":"0x0100","opcion":"op","valor":"b",
             "funciones":[{"indice":"0x01","nombre":"POWER"}],"botones":[{"codigo":"0x0C","nombre":"POWER"}]}
            """);
        Assert.Throws<InvalidDataException>(() => ProfileTests.Parse(j));
    }

    [Fact]
    public void TablaEditadaConLaOpcionQueNoLaAplicaSeRechaza()
    {
        var p = Lg();
        var fw = new byte[4 * 1024 * 1024]; // no se llega a usar: falla antes por la huella
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p,
            new Dictionary<string, string> { ["mando"] = "no", ["espejo"] = "off" },
            cancel: TestContext.Current.CancellationToken, keyMap: p.KeyTable!.Default));
        Assert.NotNull(ex.Message);
    }
}

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

    [Fact]
    public void AdmiteCodigosDeOtroMando()
    {
        // Para usar otro mando hay que poder asignar códigos que no son botones del Soyea.
        var kt = Lg().KeyTable!;
        var map = kt.Default.Take(43).Append(new KeyAssignment(0xEE, 0x01)).ToList();
        Assert.Empty(kt.Validate(map));
        Assert.Equal(map, kt.Decode(kt.Encode(map)));
        Assert.Contains("otro mando", kt.ButtonName(0xEE));
    }

    // ------------------------------------------------------------------ formatos de tabla

    private static KeyTableSpec Spec(KeyTableFormat f, int entries = 4) => new()
    {
        Format = f, Address = 0x300000, Entries = entries, CodePrefix = f == KeyTableFormat.Lista8 ? 0x100u : 0,
        Functions = new Dictionary<byte, string> { [0x01] = "A", [0x02] = "B", [0x10] = "C" },
        Buttons = [(0x05, "x"), (0x06, "y")], Default = [],
    };

    [Theory]
    [InlineData(KeyTableFormat.Lista8)]
    [InlineData(KeyTableFormat.Lista4)]
    [InlineData(KeyTableFormat.Directa256)]
    public void CodificarYDecodificarDaLoMismo(KeyTableFormat f)
    {
        var kt = Spec(f);
        var map = new List<KeyAssignment> { new(0x05, 0x01), new(0x06, 0x10), new(0x40, 0x02) };
        var bytes = kt.Encode(map);
        Assert.Equal(kt.ByteLength, bytes.Length);
        var back = kt.Decode(bytes);
        Assert.Equal(map.OrderBy(m => m.Code), back.OrderBy(m => m.Code));
    }

    [Fact]
    public void FormatoLista4ComoElKonka()
    {
        var kt = Spec(KeyTableFormat.Lista4, entries: 4);
        Assert.Equal(3, kt.MaxAssignments);
        var b = kt.Encode([new(0x02, 0x01)]);
        Assert.Equal("02010000" + "ffff0000" + "ffff0000" + "ffff0000", Convert.ToHexStringLower(b));
        Assert.Throws<InvalidDataException>(() => kt.Decode(Convert.FromHexString("0201000002010000020100000201ffff"))); // sin terminador / bytes ≠ 0
    }

    [Fact]
    public void FormatoDirecta256ComoElAtec()
    {
        var kt = Spec(KeyTableFormat.Directa256);
        Assert.Equal(256, kt.MaxAssignments);
        var b = kt.Encode([new(0x15, 0x10)]);
        Assert.Equal(0x10, b[0x15]);
        Assert.Equal(255, b.Count(x => x == 0xFF));
    }

    // ------------------------------------------------------------------ código de cliente

    [Theory]
    [InlineData("37 78", 0x37, 0x78)]
    [InlineData("3778", 0x37, 0x78)]
    [InlineData("0x00BF", 0x00, 0xBF)]
    public void LeeElCodigoDeCliente(string s, int b1, int b2)
    {
        Assert.True(NecCustomer.TryParse(s, out var c));
        Assert.Equal(new NecCustomer((byte)b1, (byte)b2), c);
        Assert.Equal($"{b1:X2} {b2:X2}", c.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("377")]
    [InlineData("37 78 00")]
    [InlineData("zz zz")]
    public void RechazaCodigosDeClienteMalEscritos(string s) => Assert.False(NecCustomer.TryParse(s, out _));

    /// <summary>Vectores de tools/aeon/aeonasm.py: asm("bg.sfeqi", {"A": r, "Y": v}).</summary>
    [Theory]
    [InlineData(7, 0x7736, "c0eee6c0")]
    [InlineData(7, 0x7837, "c0ef06e0")]
    [InlineData(7, 0x0707, "c0e0e0e0")]
    [InlineData(7, 0x5FA0, "c0ebf400")]
    [InlineData(7, 0x0000, "c0e00000")]
    [InlineData(7, 0x7FFF, "c0efffe0")]
    [InlineData(3, 0x1234, "c0624680")]
    [InlineData(31, 0x00BF, "c3e017e0")]
    [InlineData(0, 0x7F01, "c00fe020")]
    public void SfeqiIgualQueElEnsambladorDeReferencia(int reg, int imm, string hex)
    {
        Assert.Equal(hex, Convert.ToHexStringLower(ClientCodeSpec.EncodeSfeqi(reg, (ushort)imm)));
    }

    /// <summary>Vectores de tools/aeon/aeonasm.py: asm("bn.ori", {"D": r, "A": 0, "N": v}).</summary>
    [Theory]
    [InlineData(4, 0x36, "508036")]
    [InlineData(4, 0x77, "508077")]
    [InlineData(4, 0x37, "508037")]
    [InlineData(4, 0x78, "508078")]
    [InlineData(4, 0x9F, "50809f")]
    [InlineData(4, 0xFF, "5080ff")]
    [InlineData(4, 0x00, "508000")]
    [InlineData(7, 0x12, "50e012")]
    public void OriIgualQueElEnsambladorDeReferencia(int reg, int imm, string hex)
    {
        var b = ClientCodeSpec.EncodeOri(reg, (byte)imm);
        Assert.Equal(hex, Convert.ToHexStringLower(b));
        Assert.Equal((byte)imm, ClientCodeSpec.DecodeStandby(new ClientStandbyField(0, 1, reg), b));
    }

    [Fact]
    public void AtecTieneElCodigoDeReposo()
    {
        var cc = ProfileLoader.LoadBuiltin().Single(p => p.Id == "atec-32l18d-lp141wx1").ClientCode!;
        Assert.Equal(2, cc.Standby.Count);
        var soyea = new NecCustomer(0x37, 0x78);
        Assert.Equal("508037", Convert.ToHexStringLower(cc.EncodeStandby(cc.Standby.Single(f => f.Which == 1), soyea)));
        Assert.Equal("508078", Convert.ToHexStringLower(cc.EncodeStandby(cc.Standby.Single(f => f.Which == 2), soyea)));
    }

    [Theory]
    [InlineData("opcion sin reposo")]
    [InlineData("reposo distinto")]
    [InlineData("byte invalido")]
    public void PerfilConReposoIncoherenteSeRechaza(string caso)
    {
        // Zona 0x250000–0x250100; cliente principal en 0x250020 (b2 b1) y reposo en 0x250030 / 0x250034.
        var j = ProfileTests.Minimal();
        j["clienteMando"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {"direccion":"0x250020","formato":"bytes-invertidos","original":"36 77",
             "reposo":[{"direccion":"0x250030","byte":1,"registro":4},{"direccion":"0x250034","byte":2,"registro":4}]}
            """);
        var b = j["opciones"]![0]!["valores"]![1]!["parches"]!.AsArray();
        b.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250020","original":"7736","nuevo":"7837","descripcion":"c"}"""));
        switch (caso)
        {
            case "opcion sin reposo": break; // cambia el principal pero no los de reposo
            case "reposo distinto":
                b.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250030","original":"508036","nuevo":"508037","descripcion":"r1"}"""));
                b.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250034","original":"508077","nuevo":"508079","descripcion":"r2"}"""));
                break;
            case "byte invalido": j["clienteMando"]!["reposo"]![0]!["byte"] = 3; break;
        }
        Assert.Throws<InvalidDataException>(() => ProfileTests.Parse(j));
    }

    [Fact]
    public void PerfilConReposoCoherenteSeAcepta()
    {
        var j = ProfileTests.Minimal();
        j["clienteMando"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {"direccion":"0x250020","formato":"bytes-invertidos","original":"36 77",
             "reposo":[{"direccion":"0x250030","byte":1,"registro":4},{"direccion":"0x250034","byte":2,"registro":4}]}
            """);
        var b = j["opciones"]![0]!["valores"]![1]!["parches"]!.AsArray();
        b.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250020","original":"7736","nuevo":"7837","descripcion":"c"}"""));
        b.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250030","original":"508036","nuevo":"508037","descripcion":"r1"}"""));
        b.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250034","original":"508077","nuevo":"508078","descripcion":"r2"}"""));
        Assert.Equal(2, ProfileTests.Parse(j).ClientCode!.Standby.Count);
    }

    [Fact]
    public void FormatosDelCodigoDeCliente()
    {
        var soyea = new NecCustomer(0x37, 0x78);
        var inv = new ClientCodeSpec { Address = 0, Format = ClientCodeFormat.BytesInvertidos, Original = new(0x00, 0xBF) };
        Assert.Equal("bf00", Convert.ToHexStringLower(inv.OriginalBytes));           // LG original en memoria
        Assert.Equal("7837", Convert.ToHexStringLower(inv.Encode(soyea)));           // = parche probado del LG
        Assert.Equal(soyea, inv.Decode(inv.Encode(soyea)));
        var sf = new ClientCodeSpec { Address = 0, Format = ClientCodeFormat.AeonSfeqi, Register = 7, Original = new(0x36, 0x77) };
        Assert.Equal("c0eee6c0", Convert.ToHexStringLower(sf.OriginalBytes));        // ATEC original
        Assert.Equal("c0ef06e0", Convert.ToHexStringLower(sf.Encode(soyea)));        // = Soyea
        Assert.Equal(soyea, sf.Decode(sf.Encode(soyea)));
        // Y con signo: el segundo byte no puede pasar de 7F.
        Assert.NotNull(sf.WhyNot(new NecCustomer(0x00, 0xBF)));
        Assert.Throws<ArgumentException>(() => sf.Encode(new NecCustomer(0x00, 0xBF)));
        Assert.Null(sf.Decode(Convert.FromHexString("c0e000e1"))); // bits bajos ≠ 0: no es sfeqi
    }

    [Theory]
    [InlineData("formato desconocido")]
    [InlineData("fuera de zona")]
    [InlineData("sfeqi sin registro")]
    [InlineData("registro sin sfeqi")]
    [InlineData("original mal escrito")]
    [InlineData("parche que no es el campo")]
    public void PerfilConClienteMalDefinidoSeRechaza(string caso)
    {
        var j = ProfileTests.Minimal();
        j["clienteMando"] = System.Text.Json.Nodes.JsonNode.Parse("""{"direccion":"0x250000","formato":"bytes-invertidos","original":"00 BF"}""");
        var c = j["clienteMando"]!;
        switch (caso)
        {
            case "formato desconocido": c["formato"] = "bytes"; break;
            case "fuera de zona": c["direccion"] = "0x2FFFFF"; break;
            case "sfeqi sin registro": c["formato"] = "aeon-sfeqi"; c["original"] = "36 77"; break;
            case "registro sin sfeqi": c["registro"] = 7; break;
            case "original mal escrito": c["original"] = "00 BF 00"; break;
            case "parche que no es el campo": c["direccion"] = "0x250001"; break; // pisa a medias el parche fijo 0x250000
        }
        Assert.Throws<InvalidDataException>(() => ProfileTests.Parse(j));
    }

    [Fact]
    public void LosTresPerfilesTienenMandoEditable()
    {
        var ps = ProfileLoader.LoadBuiltin();
        var lg = ps.Single(p => p.Id == "lg-42lf652vx-soyea");
        var atec = ps.Single(p => p.Id == "atec-32l18d-lp141wx1");
        var konka = ps.Single(p => p.Id == "konka-tv4-32k16-mirror");
        Assert.Equal((ClientCodeFormat.BytesInvertidos, "00 BF"), (lg.ClientCode!.Format, lg.ClientCode.Original.ToString()));
        Assert.Equal((ClientCodeFormat.AeonSfeqi, "36 77"), (atec.ClientCode!.Format, atec.ClientCode.Original.ToString()));
        Assert.Equal((ClientCodeFormat.BytesInvertidos, "07 07"), (konka.ClientCode!.Format, konka.ClientCode.Original.ToString()));
        Assert.Equal((KeyTableFormat.Directa256, 48), (atec.KeyTable!.Format, atec.KeyTable.Default.Count));
        Assert.Equal((KeyTableFormat.Lista4, 45, 45), (konka.KeyTable!.Format, konka.KeyTable.Default.Count, konka.KeyTable.MaxAssignments));
        Assert.True(atec.KeyTable.InPlace && konka.KeyTable.InPlace && !lg.KeyTable!.InPlace);
        // Por defecto la tabla en su sitio reproduce exactamente los bytes originales.
        Assert.Equal(atec.KeyTable.OriginalBytes, atec.KeyTable.Encode(atec.KeyTable.Default));
        Assert.Equal(konka.KeyTable.OriginalBytes, konka.KeyTable.Encode(konka.KeyTable.Default));
    }
}

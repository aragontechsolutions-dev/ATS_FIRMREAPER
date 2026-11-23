using System.Text;
using System.Text.Json.Nodes;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Tests;

public class ProfileTests
{
    internal const string Sha = "1111111111111111111111111111111111111111111111111111111111111111";

    /// <summary>Perfil mínimo válido (base 0x243800, una zona de 0x100 bytes en 0x250000).</summary>
    internal static JsonObject Minimal(string sha = Sha) => JsonNode.Parse($$"""
    {
      "formato": 1, "id": "prueba", "version": 1, "nombre": "Prueba", "descripcion": "d", "estado": "verificado",
      "original": { "sha256": "{{sha}}" },
      "app": { "baseRam": "0x243800" },
      "compresion": { "metodo": "zopfli", "iteraciones": 1 },
      "zonasPermitidas": [ { "desde": "0x250000", "hasta": "0x250100", "motivo": "zona de prueba" } ],
      "parchesFijos": [ { "direccion": "0x250000", "original": "0001", "nuevo": "aabb", "descripcion": "p1" } ],
      "opciones": [
        { "id": "op", "etiqueta": "Op", "tipo": "lista", "porDefecto": "a",
          "valores": [
            { "id": "a", "etiqueta": "A", "parches": [] },
            { "id": "b", "etiqueta": "B", "parches": [ { "direccion": "0x250010", "original": "10", "nuevo": "ff", "descripcion": "p2" } ] }
          ] }
      ],
      "salidasConocidas": []
    }
    """)!.AsObject();

    internal static Profile Parse(JsonNode n) =>
        ProfileLoader.Parse(Encoding.UTF8.GetBytes(n.ToJsonString()), ProfileSource.Externo, "prueba.json");

    [Fact]
    public void LosPerfilesIntegradosSonValidos()
    {
        var list = ProfileLoader.LoadBuiltin();
        Assert.Contains(list, p => p.Id == "lg-42lf652vx-soyea" && p.Verified);
        Assert.Contains(list, p => p.Id == "konka-tv4-32k16-mirror" && p.Verified);
        Assert.Contains(list, p => p.Id == "atec-32l18d-lp141wx1" && p.Compression.Method == CompressionMethod.Zlib && p.Compression.Value == 9);
        Assert.Contains(list, p => p.Id == "lm2sa-v16-20210531" && p.Verified && p.Compression.Method == CompressionMethod.Zopfli);
        Assert.Contains(list, p => p.Id == "lm2sa-v16-20210614" && p.Verified && p.Compression.Method == CompressionMethod.Zopfli);
    }

    [Fact]
    public void LasSalidasConocidasEstanEnElCatalogo()
    {
        // Coherencia entre perfiles y catálogo de identificación.
        var cat = FirmwareCatalog.LoadEmbedded();
        foreach (var p in ProfileLoader.LoadBuiltin())
        {
            var orig = cat.Find(p.OriginalSha256);
            Assert.True(orig is not null && orig.Tipo == ImageKind.Original, $"{p.Id}: original no catalogado");
            foreach (var k in p.KnownOutputs)
            {
                var img = cat.Find(k.Sha256);
                Assert.True(img is not null && img.Tipo == ImageKind.Generado && img.Origen == p.OriginalSha256,
                    $"{p.Id}: salida {k.FileName} no catalogada como generada de su original");
            }
        }
    }

    [Fact]
    public void PerfilMinimoValido()
    {
        var p = Parse(Minimal());
        Assert.Single(p.FixedPatches);
        Assert.Single(p.PatchesFor(p.DefaultSelection()));
        Assert.Equal(2, p.PatchesFor(new Dictionary<string, string> { ["op"] = "b" }).Count);
        Assert.Throws<ArgumentException>(() => p.PatchesFor(new Dictionary<string, string> { ["otra"] = "a" }));
    }

    public static TheoryData<string> CasosInvalidos =>
    [
        "fuera de zona", "cruza el final de zona", "solapado con fijo", "zonas solapadas", "zona antes de la app",
        "zona enorme", "sin zonas", "no cambia", "longitudes distintas", "hex mayúsculas", "hex impar",
        "campo desconocido", "formato 2", "por defecto inexistente", "valor repetido", "dirección sin 0x",
        "compresión desconocida", "salida conocida incompleta", "id con espacios", "sha mal formada",
        "estado desconocido", "opción con un solo valor", "zlib sin nivel", "zlib con iteraciones", "zlib nivel 3",
        "zopfli con nivel",
    ];

    [Theory]
    [MemberData(nameof(CasosInvalidos))]
    public void RechazaPerfilesDañadosOManipulados(string caso)
    {
        var j = Minimal();
        var fijo = j["parchesFijos"]![0]!.AsObject();
        var op = j["opciones"]![0]!.AsObject();
        switch (caso)
        {
            case "fuera de zona": fijo["direccion"] = "0x260000"; break;
            case "cruza el final de zona": fijo["direccion"] = "0x2500FF"; break;
            case "solapado con fijo": op["valores"]![1]!["parches"]![0]!["direccion"] = "0x250001"; break;
            case "zonas solapadas": j["zonasPermitidas"]!.AsArray().Add(JsonNode.Parse("""{"desde":"0x2500F0","hasta":"0x250200","motivo":"m"}""")); break;
            case "zona antes de la app": j["zonasPermitidas"]![0]!["desde"] = "0x100000"; break;
            case "zona enorme": j["zonasPermitidas"]![0]!["hasta"] = "0x350000"; break;
            case "sin zonas": j["zonasPermitidas"] = new JsonArray(); break;
            case "no cambia": fijo["nuevo"] = "0001"; break;
            case "longitudes distintas": fijo["nuevo"] = "aabbcc"; break;
            case "hex mayúsculas": fijo["nuevo"] = "AABB"; break;
            case "hex impar": fijo["nuevo"] = "aab"; break;
            case "campo desconocido": j["escribirEnFlash"] = "0x0"; break;
            case "formato 2": j["formato"] = 2; break;
            case "por defecto inexistente": op["porDefecto"] = "z"; break;
            case "valor repetido": op["valores"]![1]!["id"] = "a"; break;
            case "dirección sin 0x": fijo["direccion"] = "250000"; break;
            case "compresión desconocida": j["compresion"]!["metodo"] = "zlib"; break;
            case "salida conocida incompleta": j["salidasConocidas"]!.AsArray().Add(JsonNode.Parse($$"""{"opciones":{},"sha256":"{{Sha}}","archivo":"x.BIN"}""")); break;
            case "id con espacios": j["id"] = "mi perfil"; break;
            case "sha mal formada": j["original"]!["sha256"] = "abc"; break;
            case "estado desconocido": j["estado"] = "probado"; break;
            case "opción con un solo valor": op["valores"]!.AsArray().RemoveAt(1); break;
            case "zlib sin nivel": j["compresion"] = JsonNode.Parse("""{"metodo":"zlib"}"""); break;
            case "zlib con iteraciones": j["compresion"] = JsonNode.Parse("""{"metodo":"zlib","nivel":9,"iteraciones":5}"""); break;
            case "zlib nivel 3": j["compresion"] = JsonNode.Parse("""{"metodo":"zlib","nivel":3}"""); break;
            case "zopfli con nivel": j["compresion"] = JsonNode.Parse("""{"metodo":"zopfli","iteraciones":5,"nivel":9}"""); break;
            default: throw new ArgumentOutOfRangeException(nameof(caso));
        }
        Assert.Throws<InvalidDataException>(() => Parse(j));
    }

    [Fact]
    public void LaCarpetaExternaRechazaInvalidosYIdsIntegrados()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fr-perfiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "bueno.json"), Minimal().ToJsonString());
            var dup = Minimal();
            dup["id"] = "lg-42lf652vx-soyea";
            File.WriteAllText(Path.Combine(dir, "duplicado.json"), dup.ToJsonString());
            File.WriteAllText(Path.Combine(dir, "roto.json"), "{ esto no es json");

            var reg = ProfileRegistry.Load(dir);
            Assert.Contains(reg.Profiles, p => p.Id == "prueba" && p.Source == ProfileSource.Externo);
            Assert.Equal(2, reg.Errors.Count);
            Assert.Single(reg.Profiles, p => p.Id == "lg-42lf652vx-soyea"); // el integrado no se sustituye
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

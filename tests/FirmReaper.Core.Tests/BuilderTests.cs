using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.MStar;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Tests;

public class BuilderTests
{
    private const uint Base = 0x243800;

    /// <summary>Byte de la app sintética en la dirección de RAM dada (ver SyntheticImage).</summary>
    private static byte AppByte(uint ram)
    {
        int i = (int)(ram - Base);
        return (byte)((i % 251) ^ (i >> 10));
    }

    /// <summary>Perfil válido para una imagen sintética concreta.</summary>
    private static Profile ProfileFor(byte[] fw, Action<JsonObject>? tweak = null)
    {
        var j = ProfileTests.Minimal(Convert.ToHexStringLower(SHA256.HashData(fw)));
        j["parchesFijos"]![0]!["original"] = $"{AppByte(0x250000):x2}{AppByte(0x250001):x2}";
        j["parchesFijos"]![0]!["nuevo"] = $"{(byte)~AppByte(0x250000):x2}{(byte)~AppByte(0x250001):x2}";
        j["opciones"]![0]!["valores"]![1]!["parches"]![0]!["original"] = $"{AppByte(0x250010):x2}";
        j["opciones"]![0]!["valores"]![1]!["parches"]![0]!["nuevo"] = $"{(byte)~AppByte(0x250010):x2}";
        tweak?.Invoke(j);
        return ProfileTests.Parse(j);
    }

    [Fact]
    public void GeneraYVerifica()
    {
        var fw = SyntheticImage.Build();
        var p = ProfileFor(fw);
        var r = FirmwareBuilder.Build(fw, p, new Dictionary<string, string> { ["op"] = "b" }, cancel: TestContext.Current.CancellationToken);

        Assert.Equal(3, r.ChangedAppBytes);
        var a = FirmwareAnalyzer.Analyze(r.Output);
        Assert.True(a.ContainerValid);
        Assert.Equal((byte)~AppByte(0x250010), a.App![0x250010 - Base]);
        // Fuera de la zona [0x20000, primer recurso) todo igual.
        Assert.True(fw.AsSpan(0, MStarLayout.HeaderOffset).SequenceEqual(r.Output.AsSpan(0, MStarLayout.HeaderOffset)));
        Assert.True(fw.AsSpan((int)SyntheticImage.FirstResource).SequenceEqual(r.Output.AsSpan((int)SyntheticImage.FirstResource)));
        Assert.Null(r.Known);
    }

    [Fact]
    public void NoModificaLaEntrada()
    {
        var fw = SyntheticImage.Build();
        var copy = (byte[])fw.Clone();
        FirmwareBuilder.Build(fw, ProfileFor(fw), new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken);
        Assert.Equal(copy, fw);
    }

    [Fact]
    public void RechazaHuellaDistinta()
    {
        var fw = SyntheticImage.Build();
        var p = ProfileFor(fw);
        fw[0x3F0000] ^= 1; // fuera del CRC: contenedor válido, pero otra huella
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p, new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken));
        Assert.Contains("huella", ex.Message);
    }

    [Fact]
    public void RechazaBytesOriginalesDistintos()
    {
        var fw = SyntheticImage.Build();
        var p = ProfileFor(fw, j => j["parchesFijos"]![0]!["original"] = "dead");
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p, new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken));
        Assert.Contains("no coinciden", ex.Message);
    }

    [Fact]
    public void DetectaParcheYaAplicado()
    {
        var fw = SyntheticImage.Build();
        // El perfil dice que el original es el valor "nuevo" que ya está: se informa como ya aplicado.
        var p = ProfileFor(fw, j =>
        {
            j["parchesFijos"]![0]!["nuevo"] = j["parchesFijos"]![0]!["original"]!.GetValue<string>();
            j["parchesFijos"]![0]!["original"] = "0000";
        });
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p, new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken));
        Assert.Contains("YA tiene", ex.Message);
    }

    [Fact]
    public void RechazaSiNoCabe()
    {
        // Primer recurso justo detrás de la app y 48 KB de bytes aleatorios en 12 parches:
        // la app recomprimida crece y ya no cabe.
        var probe = SyntheticImage.Build();
        var fw = SyntheticImage.Build(firstResource: SyntheticImage.FlashEnd(probe));
        var parches = new JsonArray();
        for (int n = 0; n < 12; n++)
        {
            uint addr = 0x250000 + (uint)n * 4096;
            var orig = new byte[4096];
            var nuevo = new byte[4096];
            RandomNumberGenerator.Fill(nuevo);
            for (int i = 0; i < 4096; i++)
            {
                orig[i] = AppByte(addr + (uint)i);
                if (nuevo[i] == orig[i]) nuevo[i] ^= 0x5A;
            }
            parches.Add(new JsonObject
            {
                ["direccion"] = $"0x{addr:X}",
                ["original"] = Convert.ToHexStringLower(orig),
                ["nuevo"] = Convert.ToHexStringLower(nuevo),
                ["descripcion"] = $"aleatorio {n}",
            });
        }
        var p = ProfileFor(fw, j =>
        {
            j["zonasPermitidas"]![0]!["hasta"] = "0x260000";
            j["parchesFijos"] = parches;
            j["opciones"] = new JsonArray();
        });
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p, new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken));
        Assert.Contains("no cabe", ex.Message);
    }

    [Fact]
    public void RechazaBaseDeAppIncorrecta()
    {
        var fw = SyntheticImage.Build();
        var p = ProfileFor(fw, j =>
        {
            j["app"]!["baseRam"] = "0x203800";
            j["zonasPermitidas"]![0]!["desde"] = "0x250000";
        });
        Assert.Throws<BuildException>(() => FirmwareBuilder.Build(fw, p, new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GuardaSinSobrescribirYConCopia()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fr-salida-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fw = SyntheticImage.Build();
            string input = Path.Combine(dir, "original.BIN");
            File.WriteAllBytes(input, fw);
            var r = FirmwareBuilder.Build(fw, ProfileFor(fw), new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken);

            var s1 = OutputWriter.Save(r, input, dir, DateTimeOffset.UnixEpoch);
            var s2 = OutputWriter.Save(r, input, dir, DateTimeOffset.UnixEpoch);

            Assert.NotEqual(s1.Firmware, s2.Firmware);           // no sobrescribe
            Assert.Equal(r.Output, File.ReadAllBytes(s1.Firmware));
            Assert.Equal(fw, File.ReadAllBytes(input));            // el original intacto
            Assert.True(s1.BackupCreated);
            Assert.False(s2.BackupCreated);                         // la copia ya existía
            Assert.Equal(fw, File.ReadAllBytes(s1.Backup));
            Assert.True(File.GetAttributes(s1.Backup).HasFlag(FileAttributes.ReadOnly));
            Assert.Contains(r.OutputSha256, File.ReadAllText(s1.ReportText));
            Assert.Contains(r.OutputSha256, File.ReadAllText(s1.ReportJson));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NoGuardaSiElOriginalCambioEnDisco()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fr-salida-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fw = SyntheticImage.Build();
            string input = Path.Combine(dir, "original.BIN");
            var r = FirmwareBuilder.Build(fw, ProfileFor(fw), new Dictionary<string, string>(), cancel: TestContext.Current.CancellationToken);
            var other = (byte[])fw.Clone();
            other[0x3F0000] ^= 1;
            File.WriteAllBytes(input, other);
            Assert.Throws<IOException>(() => OutputWriter.Save(r, input, dir, DateTimeOffset.UnixEpoch));
            Assert.Single(Directory.GetFiles(dir)); // solo el "original"
        }
        finally
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
    }
}

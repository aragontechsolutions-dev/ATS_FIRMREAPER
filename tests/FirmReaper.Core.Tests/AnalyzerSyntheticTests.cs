using System.Buffers.Binary;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.MStar;

namespace FirmReaper.Core.Tests;

/// <summary>Pruebas del análisis con imágenes sintéticas (sin firmwares reales).</summary>
public class AnalyzerSyntheticTests
{
    private static Check Get(FirmwareAnalysis a, string id) => Assert.Single(a.Checks, c => c.Id == id);

    [Fact]
    public void ImagenSinteticaCorrectaEsValida()
    {
        var fw = SyntheticImage.Build();
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.True(a.ContainerValid, string.Join("\n", a.Checks.Select(c => $"{c.Status} {c.Title}: {c.Detail}")));
        Assert.DoesNotContain(a.Checks, c => c.Status != CheckStatus.Ok);
        Assert.Equal(300_000, a.AppSize);
        Assert.Equal(SyntheticImage.FirstResource, a.Tel!.FirstResourceOffset);
        Assert.Equal("JPEG", a.Tel.Entries[1].Content);
        Assert.Equal(SyntheticImage.FirstResource - SyntheticImage.FlashEnd(fw), a.FreeBytes);
    }

    [Fact]
    public void NoModificaElArrayDeEntrada()
    {
        var fw = SyntheticImage.Build();
        var copy = (byte[])fw.Clone();
        FirmwareAnalyzer.Analyze(fw);
        Assert.Equal(copy, fw);
    }

    [Fact]
    public void DetectaCrcIncorrecto()
    {
        var fw = SyntheticImage.Build();
        fw[MStarLayout.DeflateOffset + 100] ^= 0x01;
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.False(a.ContainerValid);
        Assert.Equal(CheckStatus.Error, Get(a, "crc").Status);
    }

    [Fact]
    public void DetectaTamanoDeColaIncorrecto()
    {
        var fw = SyntheticImage.Build();
        int tail = (int)SyntheticImage.FlashEnd(fw) - 6;
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(tail), 12345);
        SyntheticImage.RefreshCrc(fw);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.Equal(CheckStatus.Ok, Get(a, "crc").Status);
        Assert.Equal(CheckStatus.Error, Get(a, "tail.size").Status);
    }

    [Fact]
    public void DetectaFinDeFlashIncoherente()
    {
        var fw = SyntheticImage.Build();
        SyntheticImage.WriteHeader(fw, SyntheticImage.FlashEnd(fw) + 2);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.Equal(CheckStatus.Error, Get(a, "hdr.endmatch").Status);
    }

    [Fact]
    public void DetectaAppQuePisaLosRecursos()
    {
        var fw = SyntheticImage.Build();
        uint end = SyntheticImage.FlashEnd(fw);
        // primer recurso movido por delante del fin de la app
        SyntheticImage.WriteTel(fw, MStarLayout.TelOffset + 14, 0x0061, end - 16, 0x1000, 0x04);
        SyntheticImage.RefreshCrc(fw);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.False(a.ContainerValid);
        Assert.Equal(CheckStatus.Error, Get(a, "hdr.fit").Status);
    }

    [Fact]
    public void RechazaRecursoFueraDelArchivo()
    {
        var fw = SyntheticImage.Build();
        SyntheticImage.WriteTel(fw, MStarLayout.TelOffset + 25, 0x0300, 0x3FFFF0, 0x100, 0x00);
        SyntheticImage.RefreshCrc(fw);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.Equal(CheckStatus.Error, Get(a, "tel").Status);
    }

    [Fact]
    public void RechazaFirmaTelAusente()
    {
        var fw = SyntheticImage.Build();
        fw[MStarLayout.TelOffset] = (byte)'X';
        SyntheticImage.RefreshCrc(fw);
        Assert.Equal(CheckStatus.Error, Get(FirmwareAnalyzer.Analyze(fw), "tel").Status);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x23881u)]
    [InlineData(0xFFFFFFFFu)]
    public void FinDeFlashFueraDeRangoNoRevienta(uint flashEnd)
    {
        var fw = SyntheticImage.Build();
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(MStarLayout.HeaderOffset + 12), flashEnd);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.False(a.ContainerValid);
    }

    [Fact]
    public void InicioDeAppDistintoEsFormatoNoSoportado()
    {
        var fw = SyntheticImage.Build();
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(MStarLayout.HeaderOffset), 0x30000);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.Equal(CheckStatus.Error, Get(a, "hdr.start").Status);
    }

    [Fact]
    public void TamanoDistintoDe4MbSeRechaza()
    {
        var a = FirmwareAnalyzer.Analyze(new byte[1000]);
        Assert.False(a.ContainerValid);
        Assert.Equal(CheckStatus.Error, Get(a, "size").Status);
    }

    [Fact]
    public void ImgInfoDesactualizadoEsSoloAviso()
    {
        var fw = SyntheticImage.Build();
        BinaryPrimitives.WriteUInt32LittleEndian(fw.AsSpan(MStarLayout.ImgInfoOffset + 0x47), 0x123456);
        SyntheticImage.RefreshCrc(fw);
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.True(a.ContainerValid);
        Assert.Equal(CheckStatus.Advertencia, Get(a, "imginfo").Status);
    }

    [Fact]
    public void ZonaLibreConDatosEsAviso()
    {
        var fw = SyntheticImage.Build();
        fw[SyntheticImage.FlashEnd(fw) + 10] = 0x55; // fuera de la zona del CRC
        var a = FirmwareAnalyzer.Analyze(fw);
        Assert.True(a.ContainerValid);
        Assert.Equal(CheckStatus.Advertencia, Get(a, "free").Status);
    }

    [Fact]
    public void BasuraNoProvocaExcepciones()
    {
        var rnd = new Random(5);
        for (int i = 0; i < 20; i++)
        {
            var fw = SyntheticImage.Build(seed: i);
            // corromper bytes al azar en las estructuras de control
            for (int k = 0; k < 8; k++)
                fw[rnd.Next(MStarLayout.HeaderOffset, MStarLayout.DeflateOffset + 64)] = (byte)rnd.Next(256);
            var a = FirmwareAnalyzer.Analyze(fw); // no debe lanzar
            Assert.NotEmpty(a.Checks);
        }
    }
}

using System.IO.Compression;
using FirmReaper.Core.Compression;

namespace FirmReaper.Core.Tests;

public class RawInflaterTests
{
    private static byte[] Deflate(byte[] data, CompressionLevel level)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, level, leaveOpen: true))
            ds.Write(data);
        return ms.ToArray();
    }

    /// <summary>Datos con mezcla de zonas repetitivas y aleatorias (ejercita literales, longitudes y distancias).</summary>
    private static byte[] Sample(int size, int seed)
    {
        var rnd = new Random(seed);
        var d = new byte[size];
        int i = 0;
        while (i < size)
        {
            int run = rnd.Next(1, 300);
            if (rnd.Next(3) == 0)
            {
                for (int k = 0; k < run && i < size; k++) d[i++] = (byte)rnd.Next(256);
            }
            else
            {
                byte b = (byte)rnd.Next(8);
                for (int k = 0; k < run && i < size; k++) d[i++] = (byte)(b + (k % 3));
            }
        }
        return d;
    }

    [Theory]
    [InlineData(1, CompressionLevel.Optimal)]
    [InlineData(1000, CompressionLevel.Fastest)]
    [InlineData(100_000, CompressionLevel.Optimal)]
    [InlineData(100_000, CompressionLevel.NoCompression)]
    [InlineData(1_000_000, CompressionLevel.SmallestSize)]
    public void DescomprimeIgualQueDotNet(int size, CompressionLevel level)
    {
        var data = Sample(size, size);
        var comp = Deflate(data, level);
        var r = RawInflater.Inflate(comp, 16 * 1024 * 1024);
        Assert.Equal(data, r.Data);
        Assert.Equal(comp.Length, r.BytesConsumed);
    }

    [Fact]
    public void InformaDondeTerminaElFlujoAunqueHayaDatosDetras()
    {
        var data = Sample(50_000, 7);
        var comp = Deflate(data, CompressionLevel.Optimal);
        var withTail = comp.Concat(new byte[] { 0xA0, 0x8F, 0x38, 0x00, 0xF8, 0x71, 0x00, 0x00 }).ToArray();
        var r = RawInflater.Inflate(withTail, 1 << 20);
        Assert.Equal(data, r.Data);
        Assert.Equal(comp.Length, r.BytesConsumed);
    }

    [Fact]
    public void EntradaVaciaNoEsDeflateValido()
    {
        // Ojo: DeflateStream de .NET 10 (zlib-ng) no escribe nada si no recibe datos; eso no es un flujo válido.
        Assert.Throws<InvalidDataException>(() => RawInflater.Inflate([], 1024));
    }

    [Fact]
    public void FlujoTruncadoLanzaExcepcion()
    {
        var comp = Deflate(Sample(50_000, 3), CompressionLevel.Optimal);
        Assert.Throws<InvalidDataException>(() => RawInflater.Inflate(comp.AsSpan(0, comp.Length / 2), 1 << 20));
    }

    [Fact]
    public void LimitaElTamanoDeSalida()
    {
        // 10 MB de ceros comprimen a muy poco: protege contra "bombas" de descompresión.
        var comp = Deflate(new byte[10 * 1024 * 1024], CompressionLevel.Optimal);
        Assert.Throws<InvalidDataException>(() => RawInflater.Inflate(comp, 1024 * 1024));
    }

    [Fact]
    public void TipoDeBloqueInvalidoLanzaExcepcion()
    {
        // BFINAL=1, BTYPE=11 (reservado)
        Assert.Throws<InvalidDataException>(() => RawInflater.Inflate([0x07, 0x00], 1024));
    }

    [Fact]
    public void BasuraAleatoriaNoProvocaOtrasExcepciones()
    {
        var rnd = new Random(42);
        for (int i = 0; i < 2000; i++)
        {
            var junk = new byte[rnd.Next(1, 200)];
            rnd.NextBytes(junk);
            try
            {
                RawInflater.Inflate(junk, 64 * 1024);
            }
            catch (InvalidDataException)
            {
                // esperado: datos inválidos
            }
        }
    }
}

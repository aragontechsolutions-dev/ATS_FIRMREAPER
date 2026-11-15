using System.Security.Cryptography;
using FirmReaper.Core.Compression;
using FirmReaper.Core.Compression.Zopfli;

namespace FirmReaper.Core.Tests;

/// <summary>
/// El port de Zopfli debe dar EXACTAMENTE los mismos bytes que py-zopfli 0.4.3
/// (zopfli.zopfli.compress(data, numiterations=N, blocksplitting=1)[2:-4]).
/// Huellas de referencia generadas con Python (ver docs/PRUEBAS.md).
/// </summary>
public class ZopfliTests
{
    /// <summary>Generador idéntico al del script Python que produjo las huellas.</summary>
    internal static byte[] Gen(string kind, int n)
    {
        switch (kind)
        {
            case "lcg":
                {
                    var o = new byte[n];
                    long x = 12345;
                    for (int i = 0; i < n; i++)
                    {
                        x = (x * 1103515245 + 12345) & 0x7fffffff;
                        o[i] = (i / 64) % 3 == 0 ? (byte)((x >> 16) & 0xff) : (byte)((i * 7) & 0x1f);
                    }
                    return o;
                }
            case "zeros":
                return new byte[n];
            case "text":
                {
                    var unit = "El veloz murcielago hindu comia feliz cardillo y kiwi. "u8.ToArray();
                    var o = new byte[n];
                    for (int i = 0; i < n; i++) o[i] = unit[i % unit.Length];
                    return o;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData("lcg", 1, 5, "bcbcf3a6578ceeeea81d15d55d78500417e0de20194632a7f77c4694a73efbdd", 3)]
    [InlineData("lcg", 100, 5, "37580438281bc59ab6233a711dbd8be3fd3b1413d6a26a175aaee1946267becc", 103)]
    [InlineData("lcg", 5000, 5, "f0f900960f600bf292c5d8fb722dcc690b9a4d25b534d5a571467ee96633dafa", 1815)]
    [InlineData("zeros", 70000, 5, "469745f8bf49bbffad03c3e734d71a1323e90e29966662f941e2c6fc03b7e2f8", 84)]
    [InlineData("text", 20000, 15, "287caf8cb3127dbec216b7277b8845c2c68d04a16933c2dd7088ca6865aafcca", 125)]
    [InlineData("lcg", 300000, 5, "6945a6d05bf199cb496c9dcf169f0e446fb1700aa12e922e887f5a2c6c50337d", 103765)]
    [InlineData("lcg", 1200000, 1, "275e40462dc2a99d0f11e8ca81a05ffcd2675bfc0b5440c669151bdead9b5397", 415294)]
    public void IdenticoAPyZopfli(string kind, int n, int iterations, string sha, int len)
    {
        var data = Gen(kind, n);
        var comp = ZopfliDeflater.Compress(data, new ZopfliOptions(NumIterations: iterations));
        Assert.Equal(len, comp.Length);
        Assert.Equal(sha, Convert.ToHexStringLower(SHA256.HashData(comp)));
        // Y además se descomprime a lo mismo.
        Assert.Equal(data, RawInflater.Inflate(comp, n + 1).Data);
    }

    [Fact]
    public void SimbolosDeDistanciaIgualesALaVersionSinClz()
    {
        // symbols.h tiene dos implementaciones (con __builtin_clz y con tablas if/else); deben coincidir.
        for (int d = 1; d <= 32768; d++)
        {
            int sym = d < 5 ? d - 1 : d < 7 ? 4 : d < 9 ? 5 : d < 13 ? 6 : d < 17 ? 7 : d < 25 ? 8 : d < 33 ? 9 :
                d < 49 ? 10 : d < 65 ? 11 : d < 97 ? 12 : d < 129 ? 13 : d < 193 ? 14 : d < 257 ? 15 : d < 385 ? 16 :
                d < 513 ? 17 : d < 769 ? 18 : d < 1025 ? 19 : d < 1537 ? 20 : d < 2049 ? 21 : d < 3073 ? 22 :
                d < 4097 ? 23 : d < 6145 ? 24 : d < 8193 ? 25 : d < 12289 ? 26 : d < 16385 ? 27 : d < 24577 ? 28 : 29;
            Assert.Equal(sym, Symbols.GetDistSymbol(d));
            int[] limits = [5, 9, 17, 33, 65, 129, 257, 513, 1025, 2049, 4097, 8193, 16385];
            int eb = 13;
            for (int k = 0; k < limits.Length; k++) if (d < limits[k]) { eb = k; break; }
            Assert.Equal(eb, Symbols.GetDistExtraBits(d));
        }
    }

    [Fact]
    public void LogaritmoExactoIgualAlDeCEnLosValoresQueUsaZopfli()
    {
        // Zopfli solo calcula log() de enteros: frecuencias y sumas de un bloque de 1 MB (< 1 000 001),
        // que al mezclar estadísticas (x + 0,5·anterior) quedan por debajo de 2 100 000.
        // ExactLog debe dar EXACTAMENTE el log() de C (glibc) que usó Python, en cualquier plataforma.
        // Huella generada con: math.log(i) empaquetado '<d' para i = 1..2 100 000 (Python en Linux).
        // (Math.Log de Windows NO coincide: por eso existe ExactLog.)
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> b = stackalloc byte[8];
        for (ulong i = 1; i <= ExactLog.GlibcCheckedLimit; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(b, ExactLog.Log(i));
            sha.AppendData(b);
        }
        Assert.Equal("6e5f0bc503fd4ac14b3c2549db0d5e81a642abe97edff35d3eb91912768b7630",
            Convert.ToHexStringLower(sha.GetHashAndReset()));
    }

    [Fact]
    public void ConstantesDeLn2()
    {
        Assert.Equal(0x3FE62E42FEFA39EFUL, (ulong)BitConverter.DoubleToInt64Bits(6.93147180559945286227e-01));
        Assert.Equal(0x3C7ABC9E3B39803FUL, (ulong)BitConverter.DoubleToInt64Bits(2.31904681384629955842e-17));
    }
}

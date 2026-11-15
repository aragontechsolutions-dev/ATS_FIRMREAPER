using System.Security.Cryptography;
using FirmReaper.Core.Compression;

namespace FirmReaper.Core.Tests;

/// <summary>
/// El port de zlib debe dar EXACTAMENTE los mismos bytes que zlib 1.3 desde Python:
/// zlib.compressobj(9, zlib.DEFLATED, -15, 8, zlib.Z_DEFAULT_STRATEGY) → compress(d) + flush().
/// "rand" = generador de 64 bits (x = x*6364136223846793005+1442695040888963407; byte = x >> 56).
/// </summary>
public class ZlibTests
{
    private static byte[] Gen(string kind, int n)
    {
        if (kind != "rand") return ZopfliTests.Gen(kind, n);
        var o = new byte[n];
        ulong x = 7;
        for (int i = 0; i < n; i++)
        {
            x = unchecked(x * 6364136223846793005UL + 1442695040888963407UL);
            o[i] = (byte)(x >> 56);
        }
        return o;
    }

    [Theory]
    [InlineData("lcg", 0, "9b4fb24edd6d1d8830e272398263cdbf026b97392cc35387b991dc0248a628f9", 2)]
    [InlineData("lcg", 1, "bcbcf3a6578ceeeea81d15d55d78500417e0de20194632a7f77c4694a73efbdd", 3)]
    [InlineData("lcg", 100, "37580438281bc59ab6233a711dbd8be3fd3b1413d6a26a175aaee1946267becc", 103)]
    [InlineData("lcg", 5000, "4e1edacdb423448aa617eac80e2afd547e890a674017f19ce07f1b1667a410b4", 1825)]
    [InlineData("zeros", 70000, "5de73a93ed1d02e62a25d5f5bb9927bb9563ea9c2ba2dc32d78e45a323bc7b38", 85)]
    [InlineData("text", 20000, "5526dcea9e8253afc217cf5564d8db3ea450f9dcffe1d0c34db5fd77da044e43", 127)]
    [InlineData("rand", 100000, "92fa5eda08ec64dd36102e622ba85ff28eda8cb2ee2d3480952e8752b19dfd5f", 100035)]
    [InlineData("lcg", 300000, "6f904cc3342b767cabce4f62df992f798ad45b42a22c080639ee7aa05ce62259", 104145)]
    [InlineData("lcg", 1200000, "928249baa941352d4160ac70a082d58b5a221f1e1e4842d4be6c2b27b2e428f3", 416111)]
    public void IdenticoAZlib13(string kind, int n, string sha, int len)
    {
        var data = Gen(kind, n);
        var comp = ZlibDeflater.Compress(data, 9);
        Assert.Equal(len, comp.Length);
        Assert.Equal(sha, Convert.ToHexStringLower(SHA256.HashData(comp)));
        Assert.Equal(data, RawInflater.Inflate(comp, n + 1).Data);
    }
}

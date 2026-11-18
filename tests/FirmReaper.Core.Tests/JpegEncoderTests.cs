using System.Security.Cryptography;
using FirmReaper.Core.Imaging;

namespace FirmReaper.Core.Tests;

/// <summary>
/// El codificador propio debe dar EXACTAMENTE el mismo JPEG que Pillow 12.3.0 (libjpeg-turbo) con
/// Image.save(..., qtables=[ql, qc], subsampling=S, optimize=False, progressive=False[, restart_marker_blocks=R]).
/// Las huellas se generaron con el script Python descrito en docs/PRUEBAS.md (misma imagen determinista).
/// </summary>
public class JpegEncoderTests
{
    /// <summary>Misma imagen que el script Python: degradados + ruido de un generador congruencial.</summary>
    internal static byte[] TestImage(int w, int h)
    {
        var b = new byte[w * h * 3];
        long s = 12345;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                s = (s * 1103515245 + 12345) & 0x7FFFFFFF;
                int n = (int)((s >> 16) & 31);
                int i = (y * w + x) * 3;
                b[i] = (byte)((x * 7 + y * 3 + n) & 255);
                b[i + 1] = (byte)((((x * y) >> 3) ^ n) & 255);
                b[i + 2] = (byte)((((x ^ y) * 5) + (n << 2)) & 255);
            }
        }
        return b;
    }

    internal static readonly int[] QL = Enumerable.Range(0, 64).Select(i => i * 3 % 40 + 2).ToArray();
    internal static readonly int[] QC = Enumerable.Range(0, 64).Select(i => i * 5 % 60 + 3).ToArray();

    [Theory]
    [InlineData(37, 29, 0, 0, "81f631abf1ed73782dd820b5991f843a569b8578ed63a69e8c052d8536bf9ee8")]
    [InlineData(37, 29, 0, 3, "cfb45133b57760b4b2f9c092e2c2d02f42d88a1e97a51e9d84b58dbade92d643")]
    [InlineData(37, 29, 1, 0, "e4713a02a2161295915ac79a2fcc4147975f670d023d5e275705b13187bb1fca")]
    [InlineData(37, 29, 1, 3, "564a7423e312970e31489d7006dbad23843a3349c4fe43873770b5e762bca2b8")]
    [InlineData(37, 29, 2, 0, "20584c41bfa4087e51771c6396897bd6c12a520206c751d734ab19efaa0ef5a3")]
    [InlineData(37, 29, 2, 3, "ba0d66d3d55393689012b93aa14428e09591c05735cd1287bb0737128262cc18")]
    [InlineData(64, 48, 0, 0, "e3a1fdf39a0279a54cca47e633ec0a5b90523aa26db0745bea7282531a2044ff")]
    [InlineData(64, 48, 0, 3, "52fbbf5252075f899523f2e142efeb74a8ab30362bf8f47e857fcf699b4d1e03")]
    [InlineData(64, 48, 1, 0, "738ad978ec93122d425b4c6f5002bfa2339942d8f7febc803b6ee45782700378")]
    [InlineData(64, 48, 1, 3, "c2cbcb1ebb597cfcda86f6e7b131408b7774f0a75072af980dd72839f2e08ad2")]
    [InlineData(64, 48, 2, 0, "179dd8bedab7a3c35cbb874d31806ca20400a78318d5f83cc6aa4dcd2502696e")]
    [InlineData(64, 48, 2, 3, "e0becef9686ec9bcdd236f2d199ce94e5cf1bcf8f346ec6cd4ea544bff850c80")]
    [InlineData(17, 9, 0, 0, "f6d90bc3b878dff1c123b224aedae05ab2cbc4bd724278adaaba6061137f26f1")]
    [InlineData(17, 9, 0, 3, "e3df176aa565609c533603614577d4c7cc5c655ad55c1d7750f45dd999f8d840")]
    [InlineData(17, 9, 1, 0, "968e8e4de1efeee05aaef916911a055a72845ba4b0517e979cbb557eb342b9c8")]
    [InlineData(17, 9, 1, 3, "40708b4032c1bb28b6d61c386865407f451c4767874ad265f78da5e057f71369")]
    [InlineData(17, 9, 2, 0, "7ee3d244194ba2a7c57b74843b5339277f3f2db5385caf20c093a5f492d6014c")]
    [InlineData(17, 9, 2, 3, "dddea58f9b71616d629c5567493d03fa09acc202d0066f060d408fc4949f8ee0")]
    [InlineData(2167, 40, 0, 0, "2f09b0aadc13a4f8c27fbeab50910868d447ebe9cfa38ce77eaaf8e7cffae5e8")]
    [InlineData(2167, 40, 0, 3, "ebbbacda42f46917ac630095e52fe85b462032c820e8595fd654e90edf9391cd")]
    [InlineData(2167, 40, 1, 0, "f38f15e00a506ade3dce70cfe4baa8227afde3d9b5a1c2d1896987ffc7ab8ef5")]
    [InlineData(2167, 40, 1, 3, "a93222e6491db6dbe798c4520692f24f378a1479d8d1633672b19170c7c3581a")]
    [InlineData(2167, 40, 2, 0, "9753958ef9df80329465ddd3244f3e7ec153bd52b54cc425a6f6c9194a4b4962")]
    [InlineData(2167, 40, 2, 3, "a265fd7c9341c006cde6b4949f5d12992c9a5f0c5243ef5aa98cb0d782447486")]
    public void IgualQuePillow(int w, int h, int subsampling, int restart, string sha)
    {
        // Pillow: subsampling 0 = 4:4:4 (Y 1x1), 1 = 4:2:2 (Y 2x1), 2 = 4:2:0 (Y 2x2)
        var (hs, vs) = subsampling switch { 0 => (1, 1), 1 => (2, 1), _ => (2, 2) };
        var j = JpegBaselineEncoder.Encode(TestImage(w, h), w, h, QL, QC, restart, hs, vs);
        Assert.Equal(sha, Convert.ToHexStringLower(SHA256.HashData(j)));

        var info = JpegInfo.Parse(j);
        Assert.Equal((hs, vs), info.EncoderSampling);
        Assert.Equal(j.Length, info.Length);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 1)]
    public void RechazaSubmuestreosNoAdmitidos(int hs, int vs)
    {
        Assert.Throws<ArgumentException>(() => JpegBaselineEncoder.Encode(TestImage(16, 16), 16, 16, QL, QC, 0, hs, vs));
    }
}

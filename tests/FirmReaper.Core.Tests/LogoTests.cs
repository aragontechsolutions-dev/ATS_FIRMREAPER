using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;
using FirmReaper.Core.Imaging;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;

namespace FirmReaper.Core.Tests;

public class LogoTests
{
    /// <summary>Formato del logo del Konka (1366x768, 4:4:4, reinicio cada 171 MCU), con tablas de prueba.</summary>
    private static readonly LogoFormat Konka = new(1366, 768, 1, 1,
        Enumerable.Repeat(16, 64).ToArray(), Enumerable.Repeat(16, 64).ToArray(), 171);

    /// <summary>JPEG baseline generado con el codificador propio.</summary>
    internal static byte[] NoiseJpeg(int w, int h, bool quality1 = false, int seed = 1, int hs = 1, int vs = 1)
    {
        var rnd = new Random(seed);
        var rgb = new byte[w * h * 3];
        if (quality1) rnd.NextBytes(rgb);
        else
            for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)((i / 3 % w) * 255 / w);
        var q = Enumerable.Repeat(quality1 ? 1 : 16, 64).ToArray();
        return JpegBaselineEncoder.Encode(rgb, w, h, q, q, quality1 ? 0 : 171, hs, vs);
    }

    [Fact]
    public void ElCodificadorProduceUnJpegQueSeDecodifica()
    {
        var j = NoiseJpeg(1366, 768);
        var info = JpegInfo.Parse(j);
        Assert.Equal((1366, 768), (info.Width, info.Height));
        Assert.True(info.IsBaseline && info.Is444 && info.Length == j.Length);
        var rgb = LogoImage.DecodeRgb(j, out int w, out int h);
        Assert.Equal(1366 * 768 * 3, rgb.Length);
        FirmwareBuilder.ValidateLogo(j, Konka, 131381); // no lanza
        Assert.Equal((1366, 768), (w, h));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void GirarConservaElFormatoDelOriginal(int hs, int vs)
    {
        var j = NoiseJpeg(2167, 1228, hs: hs, vs: vs);
        var r = LogoImage.Rotated(j, 10_000_000);
        var a = JpegInfo.Parse(j);
        var b = JpegInfo.Parse(r.Jpeg);
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        Assert.Equal(a.QuantTables[0], b.QuantTables[0]);
        Assert.Equal(a.QuantTables[1], b.QuantTables[1]);
        Assert.Equal(a.EncoderSampling, b.EncoderSampling);
        Assert.Equal(a.RestartInterval, b.RestartInterval);
        Assert.Contains("tablas de cuantización del logo original", r.Detail);
    }

    [Fact]
    public void SiNoCabeBajaLaCalidadHastaQueQuepa()
    {
        var big = NoiseJpeg(1366, 768, quality1: true);
        Assert.True(big.Length > 131381);
        var r = LogoImage.FromUserImage(big, Konka, 131381);
        Assert.True(r.Jpeg.Length <= 131381);
        Assert.Contains("calidad reducida", r.Detail);
        FirmwareBuilder.ValidateLogo(r.Jpeg, Konka, 131381);
    }

    [Fact]
    public void SiNoCabeNiConLaCalidadMinimaSeRechaza()
    {
        var big = NoiseJpeg(1366, 768, quality1: true);
        var ex = Assert.Throws<InvalidDataException>(() => LogoImage.FromUserImage(big, Konka, 2000));
        Assert.Contains("no cabe", ex.Message);
    }

    [Fact]
    public void JpegConElFormatoExactoSeUsaTalCual()
    {
        var j = NoiseJpeg(1366, 768);
        var r = LogoImage.FromUserImage(j, Konka, 131381);
        Assert.Same(j, r.Jpeg);
        Assert.Contains("tal cual", r.Detail);
    }

    [Fact]
    public void JpegConOtroSubmuestreoSeConvierteAlDelOriginal()
    {
        var j = NoiseJpeg(1366, 768, hs: 2, vs: 2); // 4:2:0; el del Konka es 4:4:4
        Assert.Contains("submuestreo", LogoImage.WhyNotDirect(j, Konka, 131381));
        var r = LogoImage.FromUserImage(j, Konka, 131381);
        Assert.Equal((1, 1), JpegInfo.Parse(r.Jpeg).EncoderSampling);
        Assert.Equal(171, JpegInfo.Parse(r.Jpeg).RestartInterval);
    }

    [Fact]
    public void ImagenDeOtroTamañoSeEncajaSinDeformar()
    {
        // 800x800 (cuadrada) en 1366x768: se reduce a 768x768 centrada, con bandas negras a los lados.
        var src = new RgbImage(Enumerable.Repeat((byte)200, 800 * 800 * 3).ToArray(), 800, 800);
        var f = LogoImage.FitInto(src, 1366, 768);
        Assert.Equal((1366, 768), (f.Width, f.Height));
        int ox = (1366 - 768) / 2;
        Assert.Equal(0, f.Pixels[(100 * 1366 + 10) * 3]);              // banda izquierda
        Assert.Equal(200, f.Pixels[(100 * 1366 + ox + 5) * 3]);        // imagen
        Assert.Equal(0, f.Pixels[(100 * 1366 + 1366 - 5) * 3]);        // banda derecha
        var r = LogoImage.FromUserImage(Bmp24(src), Konka, 131381);
        Assert.Contains("bandas negras", r.Detail);
    }

    [Fact]
    public void LeePngYBmp()
    {
        var px = new byte[5 * 3 * 3];
        for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 17);
        var img = new RgbImage(px, 5, 3);
        Assert.Equal(px, ImageDecoder.Decode(Bmp24(img)).Pixels);
        Assert.Equal(px, ImageDecoder.Decode(Png(img, colorType: 2)).Pixels);
        // RGBA con alfa 0 → negro; alfa 255 → color
        var rgba = Png(img, colorType: 6, alphaFor: i => i % 2 == 0 ? (byte)255 : (byte)0);
        var d = ImageDecoder.Decode(rgba).Pixels;
        Assert.Equal(px[0], d[0]);
        Assert.Equal(0, d[3]);
    }

    [Fact]
    public void PngDañadoSeRechaza()
    {
        var png = Png(new RgbImage(new byte[4 * 4 * 3], 4, 4), colorType: 2);
        png[^20] ^= 0xFF; // dentro de IDAT → el CRC del bloque ya no cuadra
        Assert.Throws<InvalidDataException>(() => ImageDecoder.Decode(png));
        Assert.Throws<InvalidDataException>(() => ImageDecoder.Decode("no es una imagen"u8.ToArray()));
    }

    [Fact]
    public void RechazaDimensionesDistintas()
    {
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(NoiseJpeg(800, 600), Konka, 131381));
        Assert.Contains("1366x768", ex.Message);
    }

    [Fact]
    public void RechazaProgresivo()
    {
        var j = NoiseJpeg(1366, 768);
        int sof = j.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        j[sof + 1] = 0xC2;
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(j, Konka, 131381));
        Assert.Contains("baseline", ex.Message);
    }

    [Fact]
    public void RechazaDatosTrasElFinal()
    {
        var j = NoiseJpeg(1366, 768).Concat(new byte[] { 1, 2, 3 }).ToArray();
        var ex = Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(j, Konka, 131381));
        Assert.Contains("después", ex.Message);
    }

    [Fact]
    public void RechazaJpegTruncadoONoJpeg()
    {
        var j = NoiseJpeg(1366, 768);
        Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo(j.AsSpan(0, j.Length / 2).ToArray(), Konka, 131381));
        Assert.Throws<BuildException>(() => FirmwareBuilder.ValidateLogo("no es un jpeg"u8.ToArray(), Konka, 131381));
    }

    [Fact]
    public void ElParserNoRevientaConBasura()
    {
        var rnd = new Random(3);
        var good = NoiseJpeg(64, 48);
        for (int n = 0; n < 3000; n++)
        {
            var j = (byte[])good.Clone();
            for (int k = 0; k < 4; k++) j[rnd.Next(j.Length)] = (byte)rnd.Next(256);
            try
            {
                JpegInfo.Parse(j);
            }
            catch (InvalidDataException)
            {
                // esperado
            }
        }
    }

    [Fact]
    public void LosDecodificadoresNoRevientanConBasura()
    {
        var rnd = new Random(5);
        var img = new RgbImage(Enumerable.Range(0, 16 * 16 * 3).Select(i => (byte)i).ToArray(), 16, 16);
        foreach (var good in new[] { Png(img, 2), Bmp24(img) })
        {
            for (int n = 0; n < 2000; n++)
            {
                var f = (byte[])good.Clone();
                for (int k = 0; k < 3; k++) f[rnd.Next(f.Length)] = (byte)rnd.Next(256);
                try
                {
                    ImageDecoder.Decode(f);
                }
                catch (InvalidDataException)
                {
                    // esperado
                }
            }
        }
    }

    [Fact]
    public void Crc16DeLosRecursos()
    {
        // Vector de comprobación de CRC-16/UMTS (poly 0x8005, init 0): "123456789" → 0xFEE8.
        Assert.Equal(0xFEE8, Crc16Tel.Compute("123456789"u8));
        var r = new byte[16];
        new Random(1).NextBytes(r);
        Crc16Tel.Update(r);
        Assert.True(Crc16Tel.IsValid(r));
        r[3] ^= 1;
        Assert.False(Crc16Tel.IsValid(r));
    }

    [Theory]
    [InlineData("accion desconocida")]
    [InlineData("logo sin recurso")]
    [InlineData("dos opciones de logo")]
    [InlineData("dimensiones absurdas")]
    [InlineData("tel y zona a la vez")]
    [InlineData("ni tel ni zona")]
    [InlineData("zona sobre la cabecera")]
    [InlineData("crc en zona")]
    public void PerfilConLogoMalDefinidoSeRechaza(string caso)
    {
        var j = ProfileTests.Minimal();
        var valores = j["opciones"]![0]!["valores"]!;
        j["logo"] = JsonNode.Parse("""{"idTel":"0x065E","longitudJpeg":131381,"ancho":1366,"alto":768}""");
        var zona = JsonNode.Parse("""{"desde":"0x370000","hasta":"0x3B0000","relleno":"0xFF"}""");
        switch (caso)
        {
            case "accion desconocida": valores[0]!["logo"] = "borrar"; break;
            case "logo sin recurso": valores[0]!["logo"] = "girar"; j.Remove("logo"); break;
            case "dos opciones de logo":
                valores[0]!["logo"] = "girar";
                j["opciones"]!.AsArray().Add(JsonNode.Parse("""{"id":"op2","etiqueta":"x","tipo":"lista","porDefecto":"a","valores":[{"id":"a","etiqueta":"A","parches":[],"logo":"propio"},{"id":"b","etiqueta":"B","parches":[]}]}"""));
                break;
            case "dimensiones absurdas": j["logo"]!["ancho"] = 0; break;
            case "tel y zona a la vez": j["logo"]!["zonaFlash"] = zona; break;
            case "ni tel ni zona": j["logo"]!.AsObject().Remove("idTel"); break;
            case "zona sobre la cabecera":
                j["logo"]!.AsObject().Remove("idTel");
                zona!["desde"] = "0x020000";
                j["logo"]!["zonaFlash"] = zona;
                break;
            case "crc en zona":
                j["logo"]!.AsObject().Remove("idTel");
                j["logo"]!["zonaFlash"] = zona;
                j["logo"]!["crc16"] = true;
                break;
        }
        Assert.Throws<InvalidDataException>(() => ProfileTests.Parse(j));
    }

    [Fact]
    public void PerfilConLogoValido()
    {
        var j = ProfileTests.Minimal();
        j["logo"] = JsonNode.Parse("""{"idTel":"0x065E","longitudJpeg":131381,"ancho":1366,"alto":768}""");
        j["opciones"]![0]!["valores"]![1]!["logo"] = "girar";
        var p = ProfileTests.Parse(j);
        Assert.Equal(LogoAction.Ninguna, p.LogoActionFor(new Dictionary<string, string> { ["op"] = "a" }));
        Assert.Equal(LogoAction.Girar, p.LogoActionFor(new Dictionary<string, string> { ["op"] = "b" }));
        Assert.Equal((ushort)0x065E, p.Logo!.TelId);

        j["logo"] = JsonNode.Parse("""{"zonaFlash":{"desde":"0x370000","hasta":"0x3B0000","relleno":"0xFF"},"longitudJpeg":27753,"ancho":1376,"alto":768}""");
        var z = ProfileTests.Parse(j).Logo!;
        Assert.Null(z.TelId);
        Assert.Equal((0x370000u, 0x3B0000u, (byte)0xFF), (z.FlashFrom, z.FlashTo, z.Fill));
    }

    // ------------------------------------------------------------------ utilidades

    internal static byte[] Bmp24(RgbImage img)
    {
        int stride = (img.Width * 3 + 3) & ~3;
        var f = new byte[54 + stride * img.Height];
        f[0] = (byte)'B'; f[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(2), f.Length);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(18), img.Width);
        BinaryPrimitives.WriteInt32LittleEndian(f.AsSpan(22), img.Height); // de abajo arriba
        BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(28), 24);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int s = (y * img.Width + x) * 3, d = 54 + (img.Height - 1 - y) * stride + x * 3;
                f[d] = img.Pixels[s + 2]; f[d + 1] = img.Pixels[s + 1]; f[d + 2] = img.Pixels[s];
            }
        return f;
    }

    /// <summary>PNG de 8 bits por muestra, tipo 2 (RGB) o 6 (RGBA), filtro 0 en cada fila.</summary>
    internal static byte[] Png(RgbImage img, int colorType, Func<int, byte>? alphaFor = null)
    {
        int ch = colorType == 6 ? 4 : 3;
        var raw = new MemoryStream();
        for (int y = 0; y < img.Height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < img.Width; x++)
            {
                int p = y * img.Width + x;
                raw.Write(img.Pixels, p * 3, 3);
                if (ch == 4) raw.WriteByte(alphaFor!(p));
            }
        }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw.ToArray());
        var o = new MemoryStream();
        o.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, img.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), img.Height);
        ihdr[8] = 8; ihdr[9] = (byte)colorType;
        Chunk(o, "IHDR", ihdr);
        Chunk(o, "IDAT", z.ToArray());
        Chunk(o, "IEND", []);
        return o.ToArray();
    }

    private static void Chunk(Stream o, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        o.Write(len);
        var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        o.Write(td);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, PngCrc(td));
        o.Write(crc);
    }

    /// <summary>CRC-32 del PNG (ISO-HDLC, reflejado), bit a bit; independiente del de ImageDecoder.</summary>
    private static uint PngCrc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in d)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}

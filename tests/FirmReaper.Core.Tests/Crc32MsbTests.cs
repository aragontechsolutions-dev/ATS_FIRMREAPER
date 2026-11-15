namespace FirmReaper.Core.Tests;

public class Crc32MsbTests
{
    // Valores de referencia obtenidos con crc32_nr() de los scripts Python (tools/referencia_python).
    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("123456789", 0x89A1897Fu)]
    public void CoincideConLaImplementacionPython(string text, uint expected)
    {
        Assert.Equal(expected, Crc32Msb.Compute(System.Text.Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void UnByteUnoDaElPolinomio()
    {
        Assert.Equal(Crc32Msb.Polynomial, Crc32Msb.Compute([0x01]));
    }

    [Fact]
    public void EsIncremental()
    {
        var data = new byte[1000];
        new Random(1).NextBytes(data);
        uint whole = Crc32Msb.Compute(data);
        uint parts = Crc32Msb.Compute(data.AsSpan(300), Crc32Msb.Compute(data.AsSpan(0, 300)));
        Assert.Equal(whole, parts);
    }
}

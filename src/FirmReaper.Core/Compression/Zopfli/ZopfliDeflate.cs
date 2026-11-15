// Port a C# de Zopfli (Google, Apache License 2.0) — archivos originales: deflate.c, blocksplitter.c.
// Copyright 2011 Google Inc. Ver THIRD_PARTY_NOTICES.md.
namespace FirmReaper.Core.Compression.Zopfli;

/// <summary>
/// Compresor Zopfli: produce deflate crudo (RFC 1951) idéntico al de la biblioteca C de Google
/// con las mismas opciones. Es el equivalente de <c>zopfli.zopfli.compress(data, ...)[2:-4]</c> de Python.
/// </summary>
public static class ZopfliDeflater
{
    /// <summary>Comprime <paramref name="input"/> a deflate crudo (sin cabecera zlib ni Adler-32).</summary>
    public static byte[] Compress(ReadOnlySpan<byte> input, ZopfliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.NumIterations);
        var data = input.ToArray();
        var output = new BitWriter();
        Deflate.DeflateAll(options, data, output);
        return output.ToArray();
    }
}

internal static class Deflate
{
    private static readonly uint[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    /// <summary>Si hay 0 o 1 distancias usadas, añade otra (algunos decodificadores antiguos lo necesitan).</summary>
    private static void PatchDistanceCodesForBuggyDecoders(uint[] dLengths)
    {
        int numDistCodes = 0;
        for (int i = 0; i < 30; i++)
        {
            if (dLengths[i] != 0) numDistCodes++;
            if (numDistCodes >= 2) return;
        }
        if (numDistCodes == 0)
            dLengths[0] = dLengths[1] = 1;
        else if (numDistCodes == 1)
            dLengths[dLengths[0] != 0 ? 1 : 0] = 1;
    }

    /// <summary>Codifica el árbol dinámico. Si <paramref name="output"/> es null solo calcula el tamaño en bits.</summary>
    private static long EncodeTree(uint[] llLengths, uint[] dLengths, bool use16, bool use17, bool use18, BitWriter? output)
    {
        List<uint>? rle = output is null ? null : [];
        List<uint>? rleBits = output is null ? null : [];
        uint hlit = 29; // 286 - 257
        uint hdist = 29; // 32 - 1, pero el máximo útil es 30 - 1
        var clcounts = new long[19];
        var clcl = new uint[19];
        var clsymbols = new uint[19];

        // Recorta los ceros finales.
        while (hlit > 0 && llLengths[257 + hlit - 1] == 0) hlit--;
        while (hdist > 0 && dLengths[1 + hdist - 1] == 0) hdist--;
        uint hlit2 = hlit + 257;
        uint lldTotal = hlit2 + hdist + 1;

        for (uint i = 0; i < lldTotal; i++)
        {
            // Las longitudes caben en un byte (máx. 15), como en el original.
            byte symbol = (byte)(i < hlit2 ? llLengths[i] : dLengths[i - hlit2]);
            uint count = 1;
            if (use16 || (symbol == 0 && (use17 || use18)))
            {
                for (uint j = i + 1; j < lldTotal && symbol == (j < hlit2 ? llLengths[j] : dLengths[j - hlit2]); j++)
                    count++;
            }
            i += count - 1;

            // Repeticiones de ceros con 17 y 18.
            if (symbol == 0 && count >= 3)
            {
                if (use18)
                {
                    while (count >= 11)
                    {
                        uint count2 = count > 138 ? 138 : count;
                        rle?.Add(18);
                        rleBits?.Add(count2 - 11);
                        clcounts[18]++;
                        count -= count2;
                    }
                }
                if (use17)
                {
                    while (count >= 3)
                    {
                        uint count2 = count > 10 ? 10 : count;
                        rle?.Add(17);
                        rleBits?.Add(count2 - 3);
                        clcounts[17]++;
                        count -= count2;
                    }
                }
            }

            // Repeticiones de cualquier símbolo con 16.
            if (use16 && count >= 4)
            {
                count--; // el primero se emite tal cual
                clcounts[symbol]++;
                rle?.Add(symbol);
                rleBits?.Add(0);
                while (count >= 3)
                {
                    uint count2 = count > 6 ? 6 : count;
                    rle?.Add(16);
                    rleBits?.Add(count2 - 3);
                    clcounts[16]++;
                    count -= count2;
                }
            }

            // Lo que queda sin comprimir.
            clcounts[symbol] += count;
            while (count > 0)
            {
                rle?.Add(symbol);
                rleBits?.Add(0);
                count--;
            }
        }

        Tree.CalculateBitLengths(clcounts, 19, 7, clcl);
        if (output is not null) Tree.LengthsToSymbols(clcl, 19, 7, clsymbols);

        uint hclen = 15;
        // Recorta los ceros finales.
        while (hclen > 0 && clcounts[CodeLengthOrder[hclen + 4 - 1]] == 0) hclen--;

        if (output is not null)
        {
            output.AddBits(hlit, 5);
            output.AddBits(hdist, 5);
            output.AddBits(hclen, 4);
            for (uint i = 0; i < hclen + 4; i++)
                output.AddBits(clcl[CodeLengthOrder[i]], 3);
            for (int i = 0; i < rle!.Count; i++)
            {
                uint sym = clsymbols[rle[i]];
                output.AddHuffmanBits(sym, clcl[rle[i]]);
                // Bits extra
                if (rle[i] == 16) output.AddBits(rleBits![i], 2);
                else if (rle[i] == 17) output.AddBits(rleBits![i], 3);
                else if (rle[i] == 18) output.AddBits(rleBits![i], 7);
            }
        }

        long resultSize = 14; // hlit, hdist, hclen
        resultSize += (hclen + 4) * 3;
        for (int i = 0; i < 19; i++) resultSize += clcl[i] * clcounts[i];
        resultSize += clcounts[16] * 2;
        resultSize += clcounts[17] * 3;
        resultSize += clcounts[18] * 7;
        return resultSize;
    }

    private static void AddDynamicTree(uint[] llLengths, uint[] dLengths, BitWriter output)
    {
        int best = 0;
        long bestsize = 0;
        for (int i = 0; i < 8; i++)
        {
            long size = EncodeTree(llLengths, dLengths, (i & 1) != 0, (i & 2) != 0, (i & 4) != 0, null);
            if (bestsize == 0 || size < bestsize)
            {
                bestsize = size;
                best = i;
            }
        }
        EncodeTree(llLengths, dLengths, (best & 1) != 0, (best & 2) != 0, (best & 4) != 0, output);
    }

    private static long CalculateTreeSize(uint[] llLengths, uint[] dLengths)
    {
        long result = 0;
        for (int i = 0; i < 8; i++)
        {
            long size = EncodeTree(llLengths, dLengths, (i & 1) != 0, (i & 2) != 0, (i & 4) != 0, null);
            if (result == 0 || size < result) result = size;
        }
        return result;
    }

    private static void AddLZ77Data(LZ77Store lz77, int lstart, int lend,
        uint[] llSymbols, uint[] llLengths, uint[] dSymbols, uint[] dLengths, BitWriter output)
    {
        for (int i = lstart; i < lend; i++)
        {
            uint dist = lz77.Dists[i];
            uint litlen = lz77.LitLens[i];
            if (dist == 0)
            {
                output.AddHuffmanBits(llSymbols[litlen], llLengths[litlen]);
            }
            else
            {
                int lls = Symbols.GetLengthSymbol((int)litlen);
                int ds = Symbols.GetDistSymbol((int)dist);
                output.AddHuffmanBits(llSymbols[lls], llLengths[lls]);
                output.AddBits((uint)Symbols.GetLengthExtraBitsValue((int)litlen), (uint)Symbols.GetLengthExtraBits((int)litlen));
                output.AddHuffmanBits(dSymbols[ds], dLengths[ds]);
                output.AddBits((uint)Symbols.GetDistExtraBitsValue((int)dist), (uint)Symbols.GetDistExtraBits((int)dist));
            }
        }
    }

    private static void GetFixedTree(uint[] llLengths, uint[] dLengths)
    {
        for (int i = 0; i < 144; i++) llLengths[i] = 8;
        for (int i = 144; i < 256; i++) llLengths[i] = 9;
        for (int i = 256; i < 280; i++) llLengths[i] = 7;
        for (int i = 280; i < 288; i++) llLengths[i] = 8;
        for (int i = 0; i < 32; i++) dLengths[i] = 5;
    }

    private static long CalculateBlockSymbolSizeSmall(uint[] llLengths, uint[] dLengths, LZ77Store lz77, int lstart, int lend)
    {
        long result = 0;
        for (int i = lstart; i < lend; i++)
        {
            if (lz77.Dists[i] == 0)
            {
                result += llLengths[lz77.LitLens[i]];
            }
            else
            {
                int llSymbol = Symbols.GetLengthSymbol(lz77.LitLens[i]);
                int dSymbol = Symbols.GetDistSymbol(lz77.Dists[i]);
                result += llLengths[llSymbol];
                result += dLengths[dSymbol];
                result += Symbols.GetLengthSymbolExtraBits(llSymbol);
                result += Symbols.GetDistSymbolExtraBits(dSymbol);
            }
        }
        result += llLengths[256]; // fin de bloque
        return result;
    }

    private static long CalculateBlockSymbolSizeGivenCounts(long[] llCounts, long[] dCounts,
        uint[] llLengths, uint[] dLengths, LZ77Store lz77, int lstart, int lend)
    {
        if (lstart + Z.NumLL * 3 > lend)
            return CalculateBlockSymbolSizeSmall(llLengths, dLengths, lz77, lstart, lend);

        long result = 0;
        for (int i = 0; i < 256; i++)
            result += llLengths[i] * llCounts[i];
        for (int i = 257; i < 286; i++)
        {
            result += llLengths[i] * llCounts[i];
            result += Symbols.GetLengthSymbolExtraBits(i) * llCounts[i];
        }
        for (int i = 0; i < 30; i++)
        {
            result += dLengths[i] * dCounts[i];
            result += Symbols.GetDistSymbolExtraBits(i) * dCounts[i];
        }
        result += llLengths[256]; // fin de bloque
        return result;
    }

    private static long CalculateBlockSymbolSize(uint[] llLengths, uint[] dLengths, LZ77Store lz77, int lstart, int lend)
    {
        if (lstart + Z.NumLL * 3 > lend)
            return CalculateBlockSymbolSizeSmall(llLengths, dLengths, lz77, lstart, lend);
        var llCounts = new long[Z.NumLL];
        var dCounts = new long[Z.NumD];
        lz77.GetHistogram(lstart, lend, llCounts, dCounts);
        return CalculateBlockSymbolSizeGivenCounts(llCounts, dCounts, llLengths, dLengths, lz77, lstart, lend);
    }

    private static long AbsDiff(long x, long y) => x > y ? x - y : y - x;

    /// <summary>Cambia las frecuencias para que el árbol se codifique mejor con RLE (16/17/18).</summary>
    private static void OptimizeHuffmanForRle(int length, long[] counts)
    {
        // 1) No se tocan los ceros finales.
        for (; length >= 0; --length)
        {
            if (length == 0) return;
            if (counts[length - 1] != 0) break;
        }
        // 2) Marca los tramos que ya son buenos para RLE.
        var goodForRle = new bool[length];
        long symbol = counts[0];
        int stride = 0;
        for (int i = 0; i < length + 1; ++i)
        {
            if (i == length || counts[i] != symbol)
            {
                if ((symbol == 0 && stride >= 5) || (symbol != 0 && stride >= 7))
                {
                    for (int k = 0; k < stride; ++k) goodForRle[i - k - 1] = true;
                }
                stride = 1;
                if (i != length) symbol = counts[i];
            }
            else
            {
                ++stride;
            }
        }
        // 3) Iguala las poblaciones que no son buenas para RLE.
        stride = 0;
        long limit = counts[0];
        long sum = 0;
        for (int i = 0; i < length + 1; ++i)
        {
            if (i == length || goodForRle[i] || AbsDiff(counts[i], limit) >= 4)
            {
                if (stride >= 4 || (stride >= 3 && sum == 0))
                {
                    // Cuenta media redondeada; nunca 0 si la suma no es 0.
                    int count = (int)((sum + stride / 2) / stride);
                    if (count < 1) count = 1;
                    if (sum == 0) count = 0; // no convierte ceros en no-ceros
                    for (int k = 0; k < stride; ++k) counts[i - k - 1] = count;
                }
                stride = 0;
                sum = 0;
                if (i < length - 3)
                    limit = (counts[i] + counts[i + 1] + counts[i + 2] + counts[i + 3] + 2) / 4;
                else if (i < length)
                    limit = counts[i];
                else
                    limit = 0;
            }
            ++stride;
            if (i != length) sum += counts[i];
        }
    }

    /// <summary>Prueba la optimización RLE; se queda con ella solo si el resultado es más pequeño.</summary>
    private static double TryOptimizeHuffmanForRle(LZ77Store lz77, int lstart, int lend,
        long[] llCounts, long[] dCounts, uint[] llLengths, uint[] dLengths)
    {
        var llCounts2 = (long[])llCounts.Clone();
        var dCounts2 = (long[])dCounts.Clone();
        var llLengths2 = new uint[Z.NumLL];
        var dLengths2 = new uint[Z.NumD];

        double treesize = CalculateTreeSize(llLengths, dLengths);
        double datasize = CalculateBlockSymbolSizeGivenCounts(llCounts, dCounts, llLengths, dLengths, lz77, lstart, lend);

        OptimizeHuffmanForRle(Z.NumLL, llCounts2);
        OptimizeHuffmanForRle(Z.NumD, dCounts2);
        Tree.CalculateBitLengths(llCounts2, Z.NumLL, 15, llLengths2);
        Tree.CalculateBitLengths(dCounts2, Z.NumD, 15, dLengths2);
        PatchDistanceCodesForBuggyDecoders(dLengths2);

        double treesize2 = CalculateTreeSize(llLengths2, dLengths2);
        double datasize2 = CalculateBlockSymbolSizeGivenCounts(llCounts, dCounts, llLengths2, dLengths2, lz77, lstart, lend);

        if (treesize2 + datasize2 < treesize + datasize)
        {
            Array.Copy(llLengths2, llLengths, Z.NumLL);
            Array.Copy(dLengths2, dLengths, Z.NumD);
            return treesize2 + datasize2;
        }
        return treesize + datasize;
    }

    private static double GetDynamicLengths(LZ77Store lz77, int lstart, int lend, uint[] llLengths, uint[] dLengths)
    {
        var llCounts = new long[Z.NumLL];
        var dCounts = new long[Z.NumD];
        lz77.GetHistogram(lstart, lend, llCounts, dCounts);
        llCounts[256] = 1; // fin de bloque
        Tree.CalculateBitLengths(llCounts, Z.NumLL, 15, llLengths);
        Tree.CalculateBitLengths(dCounts, Z.NumD, 15, dLengths);
        PatchDistanceCodesForBuggyDecoders(dLengths);
        return TryOptimizeHuffmanForRle(lz77, lstart, lend, llCounts, dCounts, llLengths, dLengths);
    }

    /// <summary>ZopfliCalculateBlockSize: tamaño en bits del bloque con el tipo dado (0, 1 o 2).</summary>
    public static double CalculateBlockSize(LZ77Store lz77, int lstart, int lend, int btype)
    {
        var llLengths = new uint[Z.NumLL];
        var dLengths = new uint[Z.NumD];
        double result = 3; // cabecera del bloque

        if (btype == 0)
        {
            long length = lz77.GetByteRange(lstart, lend);
            long rem = length % 65535;
            long blocks = length / 65535 + (rem != 0 ? 1 : 0);
            // Bloques sin comprimir: 5 bytes de cabecera por cada 65535 bytes.
            return blocks * 5 * 8 + length * 8;
        }
        if (btype == 1)
        {
            GetFixedTree(llLengths, dLengths);
            result += CalculateBlockSymbolSize(llLengths, dLengths, lz77, lstart, lend);
        }
        else
        {
            result += GetDynamicLengths(lz77, lstart, lend, llLengths, dLengths);
        }
        return result;
    }

    public static double CalculateBlockSizeAutoType(LZ77Store lz77, int lstart, int lend)
    {
        double uncompressedcost = CalculateBlockSize(lz77, lstart, lend, 0);
        // El árbol fijo es caro de evaluar en bloques grandes: se aproxima así.
        double fixedcost = lz77.Size > 1000 ? uncompressedcost : CalculateBlockSize(lz77, lstart, lend, 1);
        double dyncost = CalculateBlockSize(lz77, lstart, lend, 2);
        return (uncompressedcost < fixedcost && uncompressedcost < dyncost)
            ? uncompressedcost
            : (fixedcost < dyncost ? fixedcost : dyncost);
    }

    private static void AddNonCompressedBlock(bool final, byte[] input, int instart, int inend, BitWriter output)
    {
        int pos = instart;
        for (; ; )
        {
            ushort blocksize = 65535;
            if (pos + blocksize > inend) blocksize = (ushort)(inend - pos);
            bool currentfinal = pos + blocksize >= inend;
            ushort nlen = (ushort)~blocksize;

            output.AddBit(final && currentfinal ? 1 : 0);
            // BTYPE 00
            output.AddBit(0);
            output.AddBit(0);
            // Se ignoran los bits restantes del byte.
            output.Bp = 0;

            output.AppendByte((byte)(blocksize % 256));
            output.AppendByte((byte)((blocksize / 256) % 256));
            output.AppendByte((byte)(nlen % 256));
            output.AppendByte((byte)((nlen / 256) % 256));
            for (int i = 0; i < blocksize; i++) output.AppendByte(input[pos + i]);

            if (currentfinal) break;
            pos += blocksize;
        }
    }

    private static void AddLZ77Block(int btype, bool final, LZ77Store lz77, int lstart, int lend, BitWriter output)
    {
        var llLengths = new uint[Z.NumLL];
        var dLengths = new uint[Z.NumD];
        var llSymbols = new uint[Z.NumLL];
        var dSymbols = new uint[Z.NumD];

        if (btype == 0)
        {
            int length = lz77.GetByteRange(lstart, lend);
            int pos = lstart == lend ? 0 : lz77.Pos[lstart];
            int end = pos + length;
            AddNonCompressedBlock(final, lz77.Data, pos, end, output);
            return;
        }

        output.AddBit(final ? 1 : 0);
        output.AddBit(btype & 1);
        output.AddBit((btype & 2) >> 1);

        if (btype == 1)
        {
            GetFixedTree(llLengths, dLengths);
        }
        else
        {
            GetDynamicLengths(lz77, lstart, lend, llLengths, dLengths);
            AddDynamicTree(llLengths, dLengths, output);
        }

        Tree.LengthsToSymbols(llLengths, Z.NumLL, 15, llSymbols);
        Tree.LengthsToSymbols(dLengths, Z.NumD, 15, dSymbols);

        AddLZ77Data(lz77, lstart, lend, llSymbols, llLengths, dSymbols, dLengths, output);
        // Fin de bloque
        output.AddHuffmanBits(llSymbols[256], llLengths[256]);
    }

    private static void AddLZ77BlockAutoType(ZopfliOptions options, bool final, LZ77Store lz77, int lstart, int lend, BitWriter output)
    {
        double uncompressedcost = CalculateBlockSize(lz77, lstart, lend, 0);
        double fixedcost = CalculateBlockSize(lz77, lstart, lend, 1);
        double dyncost = CalculateBlockSize(lz77, lstart, lend, 2);

        // Recalcular el LZ77 para el árbol fijo es caro: solo si el fijo puede ganar.
        bool expensivefixed = lz77.Size < 1000 || fixedcost <= dyncost * 1.1;

        if (lstart == lend)
        {
            // Bloque vacío, con el árbol fijo (lo más corto).
            output.AddBits(final ? 1u : 0u, 1);
            output.AddBits(1, 2); // tipo 1
            output.AddBits(0, 7); // código de fin de bloque (256)
            return;
        }

        var fixedstore = new LZ77Store(lz77.Data);
        if (expensivefixed)
        {
            // Recalcula el LZ77 óptimo para el árbol fijo.
            int instart = lz77.Pos[lstart];
            int inend = instart + lz77.GetByteRange(lstart, lend);
            var s = new BlockState(options, instart, inend, addLmc: true);
            Squeeze.LZ77OptimalFixed(s, lz77.Data, instart, inend, fixedstore);
            fixedcost = CalculateBlockSize(fixedstore, 0, fixedstore.Size, 1);
        }

        if (uncompressedcost < fixedcost && uncompressedcost < dyncost)
            AddLZ77Block(0, final, lz77, lstart, lend, output);
        else if (fixedcost < dyncost)
        {
            if (expensivefixed)
                AddLZ77Block(1, final, fixedstore, 0, fixedstore.Size, output);
            else
                AddLZ77Block(1, final, lz77, lstart, lend, output);
        }
        else
            AddLZ77Block(2, final, lz77, lstart, lend, output);
    }

    /// <summary>ZopfliDeflatePart con btype = 2 (el único que usan los scripts: tipo automático por bloque).</summary>
    private static void DeflatePart(ZopfliOptions options, bool final, byte[] input, int instart, int inend, BitWriter output)
    {
        var splitpointsUncompressed = new List<int>();
        var splitpoints = new List<int>();
        double totalcost = 0;
        var lz77 = new LZ77Store(input);

        if (options.BlockSplitting)
            splitpointsUncompressed = BlockSplitter.BlockSplit(options, input, instart, inend, options.BlockSplittingMax);

        int npoints = splitpointsUncompressed.Count;
        for (int i = 0; i <= npoints; i++)
        {
            int start = i == 0 ? instart : splitpointsUncompressed[i - 1];
            int end = i == npoints ? inend : splitpointsUncompressed[i];
            var s = new BlockState(options, start, end, addLmc: true);
            var store = Squeeze.LZ77Optimal(s, input, start, end, options.NumIterations);
            totalcost += CalculateBlockSizeAutoType(store, 0, store.Size);

            store.AppendTo(lz77);
            if (i < npoints) splitpoints.Add(lz77.Size);
        }

        // Segunda división, ahora sobre el LZ77 óptimo; se queda con la que sea mejor.
        if (options.BlockSplitting && npoints > 1)
        {
            var splitpoints2 = BlockSplitter.BlockSplitLZ77(lz77, options.BlockSplittingMax);
            double totalcost2 = 0;
            for (int i = 0; i <= splitpoints2.Count; i++)
            {
                int start = i == 0 ? 0 : splitpoints2[i - 1];
                int end = i == splitpoints2.Count ? lz77.Size : splitpoints2[i];
                totalcost2 += CalculateBlockSizeAutoType(lz77, start, end);
            }
            if (totalcost2 < totalcost)
                splitpoints = splitpoints2;
        }

        for (int i = 0; i <= splitpoints.Count; i++)
        {
            int start = i == 0 ? 0 : splitpoints[i - 1];
            int end = i == splitpoints.Count ? lz77.Size : splitpoints[i];
            AddLZ77BlockAutoType(options, i == splitpoints.Count && final, lz77, start, end, output);
        }
    }

    /// <summary>ZopfliDeflate(options, btype=2, final=1): bloques maestros de 1 000 000 bytes.</summary>
    public static void DeflateAll(ZopfliOptions options, byte[] input, BitWriter output)
    {
        int insize = input.Length;
        int i = 0;
        do
        {
            bool masterfinal = i + Z.MasterBlockSize >= insize;
            int size = masterfinal ? insize - i : Z.MasterBlockSize;
            DeflatePart(options, masterfinal, input, i, i + size, output);
            i += size;
        } while (i < insize);
    }
}

/// <summary>blocksplitter.c — elige dónde dividir en bloques deflate.</summary>
internal static class BlockSplitter
{
    private delegate double FindMinimumFun(int i);

    /// <summary>Busca el mínimo de f en [start, end): exhaustivo si es pequeño, si no por subdivisión.</summary>
    private static int FindMinimum(FindMinimumFun f, int start, int end, out double smallest)
    {
        if (end - start < 1024)
        {
            double best = Z.LargeFloat;
            int result = start;
            for (int i = start; i < end; i++)
            {
                double v = f(i);
                if (v < best)
                {
                    best = v;
                    result = i;
                }
            }
            smallest = best;
            return result;
        }

        const int Num = 9; // buen valor: 9
        var p = new int[Num];
        var vp = new double[Num];
        double lastbest = Z.LargeFloat;
        int pos = start;
        for (; ; )
        {
            if (end - start <= Num) break;
            for (int i = 0; i < Num; i++)
            {
                p[i] = start + (i + 1) * ((end - start) / (Num + 1));
                vp[i] = f(p[i]);
            }
            int besti = 0;
            double best = vp[0];
            for (int i = 1; i < Num; i++)
            {
                if (vp[i] < best)
                {
                    best = vp[i];
                    besti = i;
                }
            }
            if (best > lastbest) break;

            start = besti == 0 ? start : p[besti - 1];
            end = besti == Num - 1 ? end : p[besti + 1];

            pos = p[besti];
            lastbest = best;
        }
        smallest = lastbest;
        return pos;
    }

    private static double EstimateCost(LZ77Store lz77, int lstart, int lend) =>
        Deflate.CalculateBlockSizeAutoType(lz77, lstart, lend);

    private static void AddSorted(int value, List<int> list)
    {
        list.Add(value);
        for (int i = 0; i + 1 < list.Count; i++)
        {
            if (list[i] > value)
            {
                for (int j = list.Count - 1; j > i; j--) list[j] = list[j - 1];
                list[i] = value;
                break;
            }
        }
    }

    private static bool FindLargestSplittableBlock(int lz77size, bool[] done, List<int> splitpoints, out int lstart, out int lend)
    {
        int longest = 0;
        bool found = false;
        lstart = 0;
        lend = 0;
        for (int i = 0; i <= splitpoints.Count; i++)
        {
            int start = i == 0 ? 0 : splitpoints[i - 1];
            int end = i == splitpoints.Count ? lz77size - 1 : splitpoints[i];
            if (!done[start] && end - start > longest)
            {
                lstart = start;
                lend = end;
                found = true;
                longest = end - start;
            }
        }
        return found;
    }

    /// <summary>ZopfliBlockSplitLZ77: puntos de división en índices del LZ77.</summary>
    public static List<int> BlockSplitLZ77(LZ77Store lz77, int maxblocks)
    {
        var splitpoints = new List<int>();
        if (lz77.Size < 10) return splitpoints; // no merece la pena

        var done = new bool[lz77.Size];
        int lstart = 0;
        int lend = lz77.Size;
        int numblocks = 1;
        for (; ; )
        {
            if (maxblocks > 0 && numblocks >= maxblocks) break;

            int cs = lstart, ce = lend;
            int llpos = FindMinimum(i => EstimateCost(lz77, cs, i) + EstimateCost(lz77, i, ce), lstart + 1, lend, out double splitcost);
            double origcost = EstimateCost(lz77, lstart, lend);

            if (splitcost > origcost || llpos == lstart + 1 || llpos == lend)
            {
                done[lstart] = true;
            }
            else
            {
                AddSorted(llpos, splitpoints);
                numblocks++;
            }

            if (!FindLargestSplittableBlock(lz77.Size, done, splitpoints, out lstart, out lend))
                break; // no quedan bloques que dividir
            if (lend - lstart < 10) break;
        }
        return splitpoints;
    }

    /// <summary>ZopfliBlockSplit: puntos de división en bytes, usando un LZ77 voraz.</summary>
    public static List<int> BlockSplit(ZopfliOptions options, byte[] input, int instart, int inend, int maxblocks)
    {
        var s = new BlockState(options, instart, inend, addLmc: false);
        var store = new LZ77Store(input);
        var h = new ZHash();
        var result = new List<int>();

        // Se usa el LZ77 voraz (no el óptimo): es más rápido y casi igual de bueno para dividir.
        LZ77.Greedy(s, input, instart, inend, store, h);
        var lz77splitpoints = BlockSplitLZ77(store, maxblocks);

        // Convierte índices LZ77 a posiciones en bytes.
        int pos = instart;
        if (lz77splitpoints.Count > 0)
        {
            for (int i = 0; i < store.Size; i++)
            {
                int length = store.Dists[i] == 0 ? 1 : store.LitLens[i];
                if (lz77splitpoints[result.Count] == i)
                {
                    result.Add(pos);
                    if (result.Count == lz77splitpoints.Count) break;
                }
                pos += length;
            }
        }
        return result;
    }
}

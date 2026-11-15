// Port a C# de Zopfli (Google, Apache License 2.0) — archivo original: squeeze.c.
// Copyright 2011 Google Inc. Ver THIRD_PARTY_NOTICES.md.
// OJO: "costs" es float (precisión simple) en el original y aquí también; los costes intermedios son double.
namespace FirmReaper.Core.Compression.Zopfli;

internal sealed class SymbolStats
{
    public readonly long[] LitLens = new long[Z.NumLL];
    public readonly long[] Dists = new long[Z.NumD];
    public readonly double[] LLSymbols = new double[Z.NumLL];
    public readonly double[] DSymbols = new double[Z.NumD];

    public void CopyTo(SymbolStats dest)
    {
        Array.Copy(LitLens, dest.LitLens, Z.NumLL);
        Array.Copy(Dists, dest.Dists, Z.NumD);
        Array.Copy(LLSymbols, dest.LLSymbols, Z.NumLL);
        Array.Copy(DSymbols, dest.DSymbols, Z.NumD);
    }

    public void ClearFreqs()
    {
        Array.Clear(LitLens);
        Array.Clear(Dists);
    }

    public void Calculate()
    {
        Tree.CalculateEntropy(LitLens, Z.NumLL, LLSymbols);
        Tree.CalculateEntropy(Dists, Z.NumD, DSymbols);
    }
}

/// <summary>Generador pseudoaleatorio de Zopfli (semilla fija: resultado determinista).</summary>
internal sealed class RanState
{
    private uint _mw = 1;
    private uint _mz = 2;

    public uint Next()
    {
        unchecked
        {
            _mz = 36969 * (_mz & 65535) + (_mz >> 16);
            _mw = 18000 * (_mw & 65535) + (_mw >> 16);
            return (_mz << 16) + _mw;
        }
    }
}

internal static class Squeeze
{
    private delegate double CostModel(int litlen, int dist);

    private static void AddWeighedStatFreqs(SymbolStats stats1, double w1, SymbolStats stats2, double w2, SymbolStats result)
    {
        for (int i = 0; i < Z.NumLL; i++)
            result.LitLens[i] = (long)(stats1.LitLens[i] * w1 + stats2.LitLens[i] * w2);
        for (int i = 0; i < Z.NumD; i++)
            result.Dists[i] = (long)(stats1.Dists[i] * w1 + stats2.Dists[i] * w2);
        result.LitLens[256] = 1; // fin de bloque
    }

    private static void RandomizeFreqs(RanState state, long[] freqs, int n)
    {
        for (int i = 0; i < n; i++)
            if ((state.Next() >> 4) % 3 == 0) freqs[i] = freqs[state.Next() % (uint)n];
    }

    private static void RandomizeStatFreqs(RanState state, SymbolStats stats)
    {
        RandomizeFreqs(state, stats.LitLens, Z.NumLL);
        RandomizeFreqs(state, stats.Dists, Z.NumD);
        stats.LitLens[256] = 1;
    }

    private static double GetCostFixed(int litlen, int dist)
    {
        if (dist == 0)
            return litlen <= 143 ? 8 : 9;
        int dbits = Symbols.GetDistExtraBits(dist);
        int lbits = Symbols.GetLengthExtraBits(litlen);
        int lsym = Symbols.GetLengthSymbol(litlen);
        int cost = lsym <= 279 ? 7 : 8;
        cost += 5; // símbolo de distancia fijo
        return cost + dbits + lbits;
    }

    private static CostModel CostStat(SymbolStats stats) => (litlen, dist) =>
    {
        if (dist == 0)
            return stats.LLSymbols[litlen];
        int lsym = Symbols.GetLengthSymbol(litlen);
        int lbits = Symbols.GetLengthExtraBits(litlen);
        int dsym = Symbols.GetDistSymbol(dist);
        int dbits = Symbols.GetDistExtraBits(dist);
        return lbits + dbits + stats.LLSymbols[lsym] + stats.DSymbols[dsym];
    };

    private static readonly int[] DSymbolsTable =
        [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];

    private static double GetCostModelMinCost(CostModel costmodel)
    {
        int bestlength = 0;
        int bestdist = 0;
        double mincost = Z.LargeFloat;
        for (int i = 3; i < 259; i++)
        {
            double c = costmodel(i, 1);
            if (c < mincost)
            {
                bestlength = i;
                mincost = c;
            }
        }
        mincost = Z.LargeFloat;
        for (int i = 0; i < 30; i++)
        {
            double c = costmodel(3, DSymbolsTable[i]);
            if (c < mincost)
            {
                bestdist = DSymbolsTable[i];
                mincost = c;
            }
        }
        return costmodel(bestlength, bestdist);
    }

    private static double GetBestLengths(BlockState s, byte[] input, int instart, int inend,
        CostModel costmodel, ushort[] lengthArray, ZHash h, float[] costs)
    {
        int blocksize = inend - instart;
        var sublen = new ushort[259];
        int windowstart = instart > Z.WindowSize ? instart - Z.WindowSize : 0;
        double mincost = GetCostModelMinCost(costmodel);

        if (instart == inend) return 0;

        h.Reset();
        h.Warmup(input, windowstart, inend);
        for (int i = windowstart; i < instart; i++)
            h.Update(input, i, inend);

        for (int i = 1; i < blocksize + 1; i++) costs[i] = (float)Z.LargeFloat;
        costs[0] = 0;
        lengthArray[0] = 0;

        for (int i = instart; i < inend; i++)
        {
            int j = i - instart; // índice en el array de costes
            h.Update(input, i, inend);

            // ZOPFLI_SHORTCUT_LONG_REPETITIONS: atajo para repeticiones muy largas del mismo byte.
            if (h.Same[i & Z.WindowMask] > Z.MaxMatch * 2
                && i > instart + Z.MaxMatch + 1
                && i + Z.MaxMatch * 2 + 1 < inend
                && h.Same[(i - Z.MaxMatch) & Z.WindowMask] > Z.MaxMatch)
            {
                double symbolcost = costmodel(Z.MaxMatch, 1);
                for (int k = 0; k < Z.MaxMatch; k++)
                {
                    costs[j + Z.MaxMatch] = (float)(costs[j] + symbolcost);
                    lengthArray[j + Z.MaxMatch] = Z.MaxMatch;
                    i++;
                    j++;
                    h.Update(input, i, inend);
                }
            }

            // La distancia no se usa aquí: cada longitud k toma su distancia de sublen[k].
            LZ77.FindLongestMatch(s, h, input, i, inend, Z.MaxMatch, sublen, out _, out ushort leng);

            // Literal
            if (i + 1 <= inend)
            {
                double newCost = costmodel(input[i], 0) + costs[j];
                if (newCost < costs[j + 1])
                {
                    costs[j + 1] = (float)newCost;
                    lengthArray[j + 1] = 1;
                }
            }
            // Coincidencias
            int kend = Math.Min(leng, inend - i);
            double mincostaddcostj = mincost + costs[j];
            for (int k = 3; k <= kend; k++)
            {
                // Atajo: nunca puede mejorar si el coste actual ya es menor que el mínimo posible.
                if (costs[j + k] <= mincostaddcostj) continue;
                double newCost = costmodel(k, sublen[k]) + costs[j];
                if (newCost < costs[j + k])
                {
                    costs[j + k] = (float)newCost;
                    lengthArray[j + k] = (ushort)k;
                }
            }
        }
        return costs[blocksize];
    }

    private static List<ushort> TraceBackwards(int size, ushort[] lengthArray)
    {
        var path = new List<ushort>();
        int index = size;
        if (size == 0) return path;
        for (; ; )
        {
            path.Add(lengthArray[index]);
            index -= lengthArray[index];
            if (index == 0) break;
        }
        path.Reverse();
        return path;
    }

    private static void FollowPath(BlockState s, byte[] input, int instart, int inend,
        List<ushort> path, LZ77Store store, ZHash h)
    {
        int windowstart = instart > Z.WindowSize ? instart - Z.WindowSize : 0;
        if (instart == inend) return;

        h.Reset();
        h.Warmup(input, windowstart, inend);
        for (int i = windowstart; i < instart; i++)
            h.Update(input, i, inend);

        int pos = instart;
        foreach (ushort pathLength in path)
        {
            ushort length = pathLength;
            h.Update(input, pos, inend);

            if (length >= Z.MinMatch)
            {
                // Recupera la distancia de la coincidencia elegida.
                LZ77.FindLongestMatch(s, h, input, pos, inend, length, null, out ushort dist, out _);
                store.StoreLitLenDist(length, dist, pos);
            }
            else
            {
                length = 1;
                store.StoreLitLenDist(input[pos], 0, pos);
            }

            for (int j = 1; j < length; j++)
                h.Update(input, pos + j, inend);
            pos += length;
        }
    }

    private static void GetStatistics(LZ77Store store, SymbolStats stats)
    {
        for (int i = 0; i < store.Size; i++)
        {
            if (store.Dists[i] == 0)
            {
                stats.LitLens[store.LitLens[i]]++;
            }
            else
            {
                stats.LitLens[Symbols.GetLengthSymbol(store.LitLens[i])]++;
                stats.Dists[Symbols.GetDistSymbol(store.Dists[i])]++;
            }
        }
        stats.LitLens[256] = 1; // fin de bloque
        stats.Calculate();
    }

    private static void LZ77OptimalRun(BlockState s, byte[] input, int instart, int inend,
        ushort[] lengthArray, CostModel costmodel, LZ77Store store, ZHash h, float[] costs)
    {
        GetBestLengths(s, input, instart, inend, costmodel, lengthArray, h, costs);
        var path = TraceBackwards(inend - instart, lengthArray);
        FollowPath(s, input, instart, inend, path, store, h);
    }

    /// <summary>ZopfliLZ77Optimal: LZ77 óptimo iterando el modelo de costes estadístico.</summary>
    public static LZ77Store LZ77Optimal(BlockState s, byte[] input, int instart, int inend, int numiterations)
    {
        int blocksize = inend - instart;
        var lengthArray = new ushort[blocksize + 1];
        var store = new LZ77Store(input);
        var currentstore = new LZ77Store(input);
        var h = new ZHash();
        var stats = new SymbolStats();
        var beststats = new SymbolStats();
        var laststats = new SymbolStats();
        var costs = new float[blocksize + 1];
        double bestcost = Z.LargeFloat;
        double lastcost = 0;
        var ranState = new RanState();
        int lastrandomstep = -1;

        // Estadísticas iniciales a partir del LZ77 voraz.
        LZ77.Greedy(s, input, instart, inend, currentstore, h);
        GetStatistics(currentstore, stats);

        for (int i = 0; i < numiterations; i++)
        {
            currentstore = new LZ77Store(input);
            LZ77OptimalRun(s, input, instart, inend, lengthArray, CostStat(stats), currentstore, h, costs);
            double cost = Deflate.CalculateBlockSize(currentstore, 0, currentstore.Size, 2);
            if (cost < bestcost)
            {
                store = currentstore.Clone();
                stats.CopyTo(beststats);
                bestcost = cost;
            }
            stats.CopyTo(laststats);
            stats.ClearFreqs();
            GetStatistics(currentstore, stats);
            if (lastrandomstep != -1)
            {
                // Mezcla con las estadísticas anteriores para no quedarse atascado.
                AddWeighedStatFreqs(stats, 1.0, laststats, 0.5, stats);
                stats.Calculate();
            }
            if (i > 5 && cost == lastcost)
            {
                beststats.CopyTo(stats);
                RandomizeStatFreqs(ranState, stats);
                stats.Calculate();
                lastrandomstep = i;
            }
            lastcost = cost;
        }
        return store;
    }

    /// <summary>ZopfliLZ77OptimalFixed: LZ77 óptimo para el árbol fijo (btype 1).</summary>
    public static void LZ77OptimalFixed(BlockState s, byte[] input, int instart, int inend, LZ77Store store)
    {
        int blocksize = inend - instart;
        var lengthArray = new ushort[blocksize + 1];
        var h = new ZHash();
        var costs = new float[blocksize + 1];
        s.BlockStart = instart;
        s.BlockEnd = inend;
        LZ77OptimalRun(s, input, instart, inend, lengthArray, GetCostFixed, store, h, costs);
    }
}

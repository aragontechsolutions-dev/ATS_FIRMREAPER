// Port a C# de Zopfli (Google, Apache License 2.0) — archivos originales: katajainen.c, tree.c.
// Copyright 2011 Google Inc. Ver THIRD_PARTY_NOTICES.md.
namespace FirmReaper.Core.Compression.Zopfli;

/// <summary>tree.c — longitudes de código, símbolos canónicos y entropía.</summary>
internal static class Tree
{
    private const double InvLog2 = 1.4426950408889;

    public static void LengthsToSymbols(uint[] lengths, int n, uint maxbits, uint[] symbols)
    {
        var blCount = new ulong[maxbits + 1];
        var nextCode = new ulong[maxbits + 1];
        for (int i = 0; i < n; i++) symbols[i] = 0;
        for (int i = 0; i < n; i++) blCount[lengths[i]]++;
        uint code = 0;
        blCount[0] = 0;
        for (uint bits = 1; bits <= maxbits; bits++)
        {
            code = (uint)((code + blCount[bits - 1]) << 1);
            nextCode[bits] = code;
        }
        for (int i = 0; i < n; i++)
        {
            uint len = lengths[i];
            if (len != 0)
            {
                symbols[i] = (uint)nextCode[len];
                nextCode[len]++;
            }
        }
    }

    /// <summary>
    /// Entropía en bits por símbolo. Usa <see cref="Math.Log"/>: el resultado debe coincidir con el
    /// log() de C para que la salida sea idéntica (lo comprueba una prueba en cada plataforma).
    /// </summary>
    public static void CalculateEntropy(long[] count, int n, double[] bitlengths)
    {
        uint sum = 0;
        for (int i = 0; i < n; i++) sum = unchecked(sum + (uint)count[i]);
        double log2sum = (sum == 0 ? Math.Log(n) : Math.Log(sum)) * InvLog2;
        for (int i = 0; i < n; i++)
        {
            if (count[i] == 0) bitlengths[i] = log2sum;
            else bitlengths[i] = log2sum - Math.Log(count[i]) * InvLog2;
            // Corrige pequeños negativos por redondeo.
            if (bitlengths[i] < 0 && bitlengths[i] > -1e-5) bitlengths[i] = 0;
        }
    }

    public static void CalculateBitLengths(long[] count, int n, int maxbits, uint[] bitlengths)
    {
        if (Katajainen.LengthLimitedCodeLengths(count, n, maxbits, bitlengths) != 0)
            throw new InvalidOperationException("zopfli: no se pudieron calcular las longitudes de código");
    }
}

/// <summary>katajainen.c — códigos de Huffman de longitud limitada (boundary package-merge).</summary>
internal static class Katajainen
{
    private struct Leaf
    {
        public long Weight;
        public int Count;
    }

    /// <summary>Nodos en arrays (índice -1 = NULL) en lugar de punteros.</summary>
    private sealed class Pool(int capacity)
    {
        public readonly long[] Weight = new long[capacity];
        public readonly int[] Tail = new int[capacity];
        public readonly int[] Count = new int[capacity];
        public int Next;

        public void Init(int node, long weight, int count, int tail)
        {
            Weight[node] = weight;
            Count[node] = count;
            Tail[node] = tail;
        }
    }

    private static void BoundaryPM(int[,] lists, Leaf[] leaves, int numsymbols, Pool pool, int index)
    {
        int lastcount = pool.Count[lists[index, 1]];

        // Ya no quedan hojas ni listas que usar.
        if (index == 0 && lastcount >= numsymbols) return;

        int newchain = pool.Next++;
        int oldchain = lists[index, 1];

        lists[index, 0] = oldchain;
        lists[index, 1] = newchain;

        if (index == 0)
        {
            pool.Init(newchain, leaves[lastcount].Weight, lastcount + 1, -1);
        }
        else
        {
            long sum = pool.Weight[lists[index - 1, 0]] + pool.Weight[lists[index - 1, 1]];
            if (lastcount < numsymbols && sum > leaves[lastcount].Weight)
            {
                pool.Init(newchain, leaves[lastcount].Weight, lastcount + 1, pool.Tail[oldchain]);
            }
            else
            {
                pool.Init(newchain, sum, lastcount, lists[index - 1, 1]);
                BoundaryPM(lists, leaves, numsymbols, pool, index - 1);
                BoundaryPM(lists, leaves, numsymbols, pool, index - 1);
            }
        }
    }

    private static void BoundaryPMFinal(int[,] lists, Leaf[] leaves, int numsymbols, Pool pool, int index)
    {
        int lastcount = pool.Count[lists[index, 1]];
        long sum = pool.Weight[lists[index - 1, 0]] + pool.Weight[lists[index - 1, 1]];

        if (lastcount < numsymbols && sum > leaves[lastcount].Weight)
        {
            int newchain = pool.Next;
            int oldchain = pool.Tail[lists[index, 1]];
            lists[index, 1] = newchain;
            pool.Count[newchain] = lastcount + 1;
            pool.Tail[newchain] = oldchain;
        }
        else
        {
            pool.Tail[lists[index, 1]] = lists[index - 1, 1];
        }
    }

    private static void InitLists(Pool pool, Leaf[] leaves, int maxbits, int[,] lists)
    {
        int node0 = pool.Next++;
        int node1 = pool.Next++;
        pool.Init(node0, leaves[0].Weight, 1, -1);
        pool.Init(node1, leaves[1].Weight, 2, -1);
        for (int i = 0; i < maxbits; i++)
        {
            lists[i, 0] = node0;
            lists[i, 1] = node1;
        }
    }

    private static void ExtractBitLengths(int chain, Pool pool, Leaf[] leaves, uint[] bitlengths)
    {
        var counts = new int[16];
        uint end = 16;
        uint ptr = 15;
        uint value = 1;

        for (int node = chain; node != -1; node = pool.Tail[node])
            counts[--end] = pool.Count[node];

        int val = counts[15];
        while (ptr >= end)
        {
            for (; val > counts[ptr - 1]; val--)
                bitlengths[leaves[val - 1].Count] = value;
            ptr--;
            value++;
        }
    }

    public static int LengthLimitedCodeLengths(long[] frequencies, int n, int maxbits, uint[] bitlengths)
    {
        var leaves = new Leaf[n];
        int numsymbols = 0;

        for (int i = 0; i < n; i++) bitlengths[i] = 0;

        // Una hoja por símbolo con frecuencia no nula.
        for (int i = 0; i < n; i++)
        {
            if (frequencies[i] != 0)
            {
                leaves[numsymbols].Weight = frequencies[i];
                leaves[numsymbols].Count = i; // índice del símbolo
                numsymbols++;
            }
        }

        if ((1 << maxbits) < numsymbols) return 1;
        if (numsymbols == 0) return 0;
        if (numsymbols == 1)
        {
            bitlengths[leaves[0].Count] = 1;
            return 0;
        }
        if (numsymbols == 2)
        {
            bitlengths[leaves[0].Count]++;
            bitlengths[leaves[1].Count]++;
            return 0;
        }

        // Ordena por peso; el índice en los 9 bits bajos deshace empates (como el original).
        for (int i = 0; i < numsymbols; i++)
        {
            if (leaves[i].Weight >= (1L << (64 - 9))) return 1;
            leaves[i].Weight = (leaves[i].Weight << 9) | (long)leaves[i].Count;
        }
        // Comparador idéntico al de C: diferencia truncada a int.
        Array.Sort(leaves, 0, numsymbols, Comparer<Leaf>.Create((a, b) => unchecked((int)(a.Weight - b.Weight))));
        for (int i = 0; i < numsymbols; i++) leaves[i].Weight >>= 9;

        if (numsymbols - 1 < maxbits) maxbits = numsymbols - 1;

        var pool = new Pool(maxbits * 2 * numsymbols);
        var lists = new int[maxbits, 2];
        InitLists(pool, leaves, maxbits, lists);

        // Hay 2n-2 cadenas activas; 2 se añaden en InitLists y la última en BoundaryPMFinal.
        int numBoundaryPMRuns = 2 * numsymbols - 4;
        for (int i = 0; i < numBoundaryPMRuns - 1; i++)
            BoundaryPM(lists, leaves, numsymbols, pool, maxbits - 1);
        BoundaryPMFinal(lists, leaves, numsymbols, pool, maxbits - 1);

        ExtractBitLengths(lists[maxbits - 1, 1], pool, leaves, bitlengths);
        return 0;
    }
}

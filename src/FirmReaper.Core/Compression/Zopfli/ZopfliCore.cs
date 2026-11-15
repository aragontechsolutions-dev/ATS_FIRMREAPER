// Port a C# de Zopfli (Google, Apache License 2.0) — archivos originales: util.h, hash.c, cache.c, lz77.c.
// Copyright 2011 Google Inc. Ver THIRD_PARTY_NOTICES.md.
// Port fiel: mismos tipos efectivos, mismo orden de operaciones (el resultado debe ser idéntico byte a byte).
namespace FirmReaper.Core.Compression.Zopfli;

/// <summary>Opciones de Zopfli (las mismas que usa <c>zopfli.zopfli.compress</c> de Python).</summary>
/// <param name="NumIterations">Iteraciones del modelo de costes (Python: numiterations; los scripts usan 5).</param>
/// <param name="BlockSplitting">División en bloques (Python: blocksplitting=1).</param>
/// <param name="BlockSplittingMax">Máximo de bloques (Python: blocksplittingmax=15).</param>
public sealed record ZopfliOptions(int NumIterations = 15, bool BlockSplitting = true, int BlockSplittingMax = 15);

internal static class Z
{
    public const int MaxMatch = 258;
    public const int MinMatch = 3;
    public const int NumLL = 288;
    public const int NumD = 32;
    public const int WindowSize = 32768;
    public const int WindowMask = WindowSize - 1;
    public const int MasterBlockSize = 1000000;
    public const double LargeFloat = 1e30;
    public const int CacheLength = 8;
    public const int MaxChainHits = 8192;
}

/// <summary>Salida de bits de deflate (AddBit/AddBits/AddHuffmanBits de deflate.c).</summary>
internal sealed class BitWriter
{
    private byte[] _buf = new byte[1 << 16];
    public int Size { get; private set; }
    /// <summary>Posición del bit dentro del último byte (bp en el original).</summary>
    public byte Bp;

    public void AppendByte(byte b)
    {
        if (Size == _buf.Length) Array.Resize(ref _buf, _buf.Length * 2);
        _buf[Size++] = b;
    }

    public void AddBit(int bit)
    {
        if (Bp == 0) AppendByte(0);
        _buf[Size - 1] |= (byte)(bit << Bp);
        Bp = (byte)((Bp + 1) & 7);
    }

    public void AddBits(uint symbol, uint length)
    {
        for (uint i = 0; i < length; i++)
        {
            uint bit = (symbol >> (int)i) & 1;
            if (Bp == 0) AppendByte(0);
            _buf[Size - 1] |= (byte)(bit << Bp);
            Bp = (byte)((Bp + 1) & 7);
        }
    }

    public void AddHuffmanBits(uint symbol, uint length)
    {
        for (uint i = 0; i < length; i++)
        {
            uint bit = (symbol >> (int)(length - i - 1)) & 1;
            if (Bp == 0) AppendByte(0);
            _buf[Size - 1] |= (byte)(bit << Bp);
            Bp = (byte)((Bp + 1) & 7);
        }
    }

    public byte[] ToArray() => _buf.AsSpan(0, Size).ToArray();
}

/// <summary>hash.c — tabla hash encadenada con ZOPFLI_HASH_SAME y ZOPFLI_HASH_SAME_HASH activados.</summary>
internal sealed class ZHash
{
    private const int HashShift = 5;
    private const int HashMask = 32767;

    public readonly int[] Head = new int[65536];
    public readonly ushort[] Prev = new ushort[Z.WindowSize];
    public readonly int[] HashVal = new int[Z.WindowSize];
    public int Val;
    public readonly int[] Head2 = new int[65536];
    public readonly ushort[] Prev2 = new ushort[Z.WindowSize];
    public readonly int[] HashVal2 = new int[Z.WindowSize];
    public int Val2;
    public readonly ushort[] Same = new ushort[Z.WindowSize];

    public void Reset()
    {
        Val = 0;
        Array.Fill(Head, -1);
        for (int i = 0; i < Z.WindowSize; i++)
        {
            Prev[i] = (ushort)i;
            HashVal[i] = -1;
        }
        Array.Clear(Same);
        Val2 = 0;
        Array.Fill(Head2, -1);
        for (int i = 0; i < Z.WindowSize; i++)
        {
            Prev2[i] = (ushort)i;
            HashVal2[i] = -1;
        }
    }

    private void UpdateHashValue(byte c) => Val = ((Val << HashShift) ^ c) & HashMask;

    public void Update(byte[] array, int pos, int end)
    {
        ushort hpos = (ushort)(pos & Z.WindowMask);
        int amount = 0;

        UpdateHashValue(pos + Z.MinMatch <= end ? array[pos + Z.MinMatch - 1] : (byte)0);
        HashVal[hpos] = Val;
        if (Head[Val] != -1 && HashVal[Head[Val]] == Val)
            Prev[hpos] = (ushort)Head[Val];
        else
            Prev[hpos] = hpos;
        Head[Val] = hpos;

        if (Same[(pos - 1) & Z.WindowMask] > 1)
            amount = Same[(pos - 1) & Z.WindowMask] - 1;
        while (pos + amount + 1 < end && array[pos] == array[pos + amount + 1] && amount < ushort.MaxValue)
            amount++;
        Same[hpos] = (ushort)amount;

        Val2 = ((Same[hpos] - Z.MinMatch) & 255) ^ Val;
        HashVal2[hpos] = Val2;
        if (Head2[Val2] != -1 && HashVal2[Head2[Val2]] == Val2)
            Prev2[hpos] = (ushort)Head2[Val2];
        else
            Prev2[hpos] = hpos;
        Head2[Val2] = hpos;
    }

    public void Warmup(byte[] array, int pos, int end)
    {
        UpdateHashValue(array[pos]);
        if (pos + 1 < end) UpdateHashValue(array[pos + 1]);
    }
}

/// <summary>cache.c — caché de la coincidencia más larga.</summary>
internal sealed class LongestMatchCache
{
    public readonly ushort[] Length;
    public readonly ushort[] Dist;
    public readonly byte[] Sublen;

    public LongestMatchCache(int blocksize)
    {
        Length = new ushort[blocksize];
        Dist = new ushort[blocksize];
        Sublen = new byte[Z.CacheLength * 3 * blocksize];
        Array.Fill(Length, (ushort)1);
    }

    public void SublenToCache(ushort[] sublen, int pos, int length)
    {
        int cache = Z.CacheLength * pos * 3;
        int j = 0;
        if (length < 3) return;
        for (int i = 3; i <= length; i++)
        {
            if (i == length || sublen[i] != sublen[i + 1])
            {
                Sublen[cache + j * 3] = (byte)(i - 3);
                Sublen[cache + j * 3 + 1] = (byte)(sublen[i] % 256);
                Sublen[cache + j * 3 + 2] = (byte)((sublen[i] >> 8) % 256);
                j++;
                if (j >= Z.CacheLength) break;
            }
        }
        if (j < Z.CacheLength)
            Sublen[cache + (Z.CacheLength - 1) * 3] = (byte)(length - 3);
    }

    public void CacheToSublen(int pos, int length, ushort[] sublen)
    {
        int maxlength = MaxCachedSublen(pos);
        int prevlength = 0;
        if (length < 3) return;
        int cache = Z.CacheLength * pos * 3;
        for (int j = 0; j < Z.CacheLength; j++)
        {
            int len = Sublen[cache + j * 3] + 3;
            int dist = Sublen[cache + j * 3 + 1] + 256 * Sublen[cache + j * 3 + 2];
            for (int i = prevlength; i <= len; i++)
                sublen[i] = (ushort)dist;
            if (len == maxlength) break;
            prevlength = len + 1;
        }
    }

    public int MaxCachedSublen(int pos)
    {
        int cache = Z.CacheLength * pos * 3;
        if (Sublen[cache + 1] == 0 && Sublen[cache + 2] == 0) return 0;
        return Sublen[cache + (Z.CacheLength - 1) * 3] + 3;
    }
}

/// <summary>lz77.h — ZopfliLZ77Store.</summary>
internal sealed class LZ77Store
{
    public ushort[] LitLens = new ushort[16];
    public ushort[] Dists = new ushort[16];
    public int[] Pos = new int[16];
    public ushort[] LLSymbol = new ushort[16];
    public ushort[] DSymbol = new ushort[16];
    public long[] LLCounts = [];
    public long[] DCounts = [];
    public int Size;
    public readonly byte[] Data;

    public LZ77Store(byte[] data) => Data = data;

    public LZ77Store Clone()
    {
        var d = new LZ77Store(Data)
        {
            Size = Size,
            LitLens = LitLens.AsSpan(0, Size).ToArray(),
            Dists = Dists.AsSpan(0, Size).ToArray(),
            Pos = Pos.AsSpan(0, Size).ToArray(),
            LLSymbol = LLSymbol.AsSpan(0, Size).ToArray(),
            DSymbol = DSymbol.AsSpan(0, Size).ToArray(),
            LLCounts = LLCounts.AsSpan(0, Z.NumLL * CeilDiv(Size, Z.NumLL)).ToArray(),
            DCounts = DCounts.AsSpan(0, Z.NumD * CeilDiv(Size, Z.NumD)).ToArray(),
        };
        return d;
    }

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    private void EnsureCapacity(int n)
    {
        if (n <= LitLens.Length) return;
        int cap = Math.Max(n, LitLens.Length * 2);
        Array.Resize(ref LitLens, cap);
        Array.Resize(ref Dists, cap);
        Array.Resize(ref Pos, cap);
        Array.Resize(ref LLSymbol, cap);
        Array.Resize(ref DSymbol, cap);
    }

    public void StoreLitLenDist(ushort length, ushort dist, int pos)
    {
        int origsize = Size;
        int llstart = Z.NumLL * (origsize / Z.NumLL);
        int dstart = Z.NumD * (origsize / Z.NumD);

        // Cada NUM_LL (o NUM_D) símbolos se abre un nuevo bloque de histograma acumulado.
        if (origsize % Z.NumLL == 0)
        {
            if (LLCounts.Length < origsize + Z.NumLL) Array.Resize(ref LLCounts, Math.Max(origsize + Z.NumLL, LLCounts.Length * 2));
            for (int i = 0; i < Z.NumLL; i++)
                LLCounts[origsize + i] = origsize == 0 ? 0 : LLCounts[origsize - Z.NumLL + i];
        }
        if (origsize % Z.NumD == 0)
        {
            if (DCounts.Length < origsize + Z.NumD) Array.Resize(ref DCounts, Math.Max(origsize + Z.NumD, DCounts.Length * 2));
            for (int i = 0; i < Z.NumD; i++)
                DCounts[origsize + i] = origsize == 0 ? 0 : DCounts[origsize - Z.NumD + i];
        }

        EnsureCapacity(origsize + 1);
        LitLens[origsize] = length;
        Dists[origsize] = dist;
        Pos[origsize] = pos;
        if (dist == 0)
        {
            LLSymbol[origsize] = length;
            DSymbol[origsize] = 0;
            LLCounts[llstart + length]++;
        }
        else
        {
            int ls = Symbols.GetLengthSymbol(length);
            int ds = Symbols.GetDistSymbol(dist);
            LLSymbol[origsize] = (ushort)ls;
            DSymbol[origsize] = (ushort)ds;
            LLCounts[llstart + ls]++;
            DCounts[dstart + ds]++;
        }
        Size = origsize + 1;
    }

    public void AppendTo(LZ77Store target)
    {
        for (int i = 0; i < Size; i++)
            target.StoreLitLenDist(LitLens[i], Dists[i], Pos[i]);
    }

    public int GetByteRange(int lstart, int lend)
    {
        int l = lend - 1;
        if (lstart == lend) return 0;
        return Pos[l] + (Dists[l] == 0 ? 1 : LitLens[l]) - Pos[lstart];
    }

    private void GetHistogramAt(int lpos, long[] llCounts, long[] dCounts)
    {
        int llpos = Z.NumLL * (lpos / Z.NumLL);
        int dpos = Z.NumD * (lpos / Z.NumD);
        for (int i = 0; i < Z.NumLL; i++)
            llCounts[i] = LLCounts[llpos + i];
        for (int i = lpos + 1; i < llpos + Z.NumLL && i < Size; i++)
            llCounts[LLSymbol[i]]--;
        for (int i = 0; i < Z.NumD; i++)
            dCounts[i] = DCounts[dpos + i];
        for (int i = lpos + 1; i < dpos + Z.NumD && i < Size; i++)
            if (Dists[i] != 0) dCounts[DSymbol[i]]--;
    }

    public void GetHistogram(int lstart, int lend, long[] llCounts, long[] dCounts)
    {
        if (lstart + Z.NumLL * 3 > lend)
        {
            Array.Clear(llCounts, 0, Z.NumLL);
            Array.Clear(dCounts, 0, Z.NumD);
            for (int i = lstart; i < lend; i++)
            {
                llCounts[LLSymbol[i]]++;
                if (Dists[i] != 0) dCounts[DSymbol[i]]++;
            }
        }
        else
        {
            GetHistogramAt(lend - 1, llCounts, dCounts);
            if (lstart > 0)
            {
                var ll2 = new long[Z.NumLL];
                var d2 = new long[Z.NumD];
                GetHistogramAt(lstart - 1, ll2, d2);
                for (int i = 0; i < Z.NumLL; i++) llCounts[i] -= ll2[i];
                for (int i = 0; i < Z.NumD; i++) dCounts[i] -= d2[i];
            }
        }
    }
}

/// <summary>lz77.h — ZopfliBlockState.</summary>
internal sealed class BlockState(ZopfliOptions options, int blockstart, int blockend, bool addLmc)
{
    public readonly ZopfliOptions Options = options;
    public readonly LongestMatchCache? Lmc = addLmc ? new LongestMatchCache(blockend - blockstart) : null;
    public int BlockStart = blockstart;
    public int BlockEnd = blockend;
}

/// <summary>lz77.c — búsqueda de coincidencias y LZ77 voraz.</summary>
internal static class LZ77
{
    private static int GetLengthScore(int length, int distance) => distance > 1024 ? length - 1 : length;

    /// <summary>Equivalente a GetMatch(): cuenta bytes iguales desde scan/match hasta end.</summary>
    private static int GetMatch(byte[] a, int scan, int match, int end, int safeEnd)
    {
        while (scan < safeEnd)
        {
            if (BitConverter.ToUInt64(a, scan) != BitConverter.ToUInt64(a, match)) break;
            scan += 8;
            match += 8;
        }
        while (scan != end && a[scan] == a[match])
        {
            scan++;
            match++;
        }
        return scan;
    }

    private static bool TryGetFromLongestMatchCache(BlockState s, int pos, ref int limit,
        ushort[]? sublen, out ushort distance, out ushort length)
    {
        distance = 0;
        length = 0;
        var lmc = s.Lmc;
        if (lmc is null) return false;
        int lmcpos = pos - s.BlockStart;
        bool cacheAvailable = lmc.Length[lmcpos] == 0 || lmc.Dist[lmcpos] != 0;
        bool limitOkForCache = cacheAvailable &&
            (limit == Z.MaxMatch || lmc.Length[lmcpos] <= limit ||
             (sublen is not null && lmc.MaxCachedSublen(lmcpos) >= limit));

        if (limitOkForCache && cacheAvailable)
        {
            if (sublen is null || lmc.Length[lmcpos] <= lmc.MaxCachedSublen(lmcpos))
            {
                length = lmc.Length[lmcpos];
                if (length > limit) length = (ushort)limit;
                if (sublen is not null)
                {
                    lmc.CacheToSublen(lmcpos, length, sublen);
                    distance = sublen[length];
                }
                else
                {
                    distance = lmc.Dist[lmcpos];
                }
                return true;
            }
            // Puede ocurrir: el máximo de la caché es menor que la longitud pedida.
            limit = lmc.Length[lmcpos];
        }
        return false;
    }

    private static void StoreInLongestMatchCache(BlockState s, int pos, int limit,
        ushort[]? sublen, ushort distance, ushort length)
    {
        var lmc = s.Lmc;
        if (lmc is null) return;
        int lmcpos = pos - s.BlockStart;
        bool cacheAvailable = lmc.Length[lmcpos] == 0 || lmc.Dist[lmcpos] != 0;
        if (limit == Z.MaxMatch && sublen is not null && !cacheAvailable)
        {
            lmc.Dist[lmcpos] = length < Z.MinMatch ? (ushort)0 : distance;
            lmc.Length[lmcpos] = length < Z.MinMatch ? (ushort)0 : length;
            lmc.SublenToCache(sublen, lmcpos, length);
        }
    }

    public static void FindLongestMatch(BlockState s, ZHash h, byte[] array, int pos, int size, int limit,
        ushort[]? sublen, out ushort distance, out ushort length)
    {
        ushort hpos = (ushort)(pos & Z.WindowMask);
        ushort bestdist = 0;
        ushort bestlength = 1;
        int chainCounter = Z.MaxChainHits;
        int[] hhead = h.Head;
        ushort[] hprev = h.Prev;
        int hval = h.Val;

        if (TryGetFromLongestMatchCache(s, pos, ref limit, sublen, out distance, out length))
            return;

        if (size - pos < Z.MinMatch)
        {
            length = 0;
            distance = 0;
            return;
        }

        if (pos + limit > size) limit = size - pos;
        int arrayend = pos + limit;
        int arrayendSafe = arrayend - 8;

        ushort pp = (ushort)hhead[hval];
        ushort p = hprev[pp];
        int dist = p < pp ? pp - p : ((Z.WindowSize - p) + pp);

        while (dist < Z.WindowSize)
        {
            ushort currentlength = 0;
            if (dist > 0)
            {
                int scan = pos;
                int match = pos - dist;
                if (pos + bestlength >= size || array[scan + bestlength] == array[match + bestlength])
                {
                    ushort same0 = h.Same[pos & Z.WindowMask];
                    if (same0 > 2 && array[scan] == array[match])
                    {
                        ushort same1 = h.Same[(pos - dist) & Z.WindowMask];
                        int same = same0 < same1 ? same0 : same1;
                        if (same > limit) same = limit;
                        scan += same;
                        match += same;
                    }
                    scan = GetMatch(array, scan, match, arrayend, arrayendSafe);
                    currentlength = (ushort)(scan - pos);
                }

                if (currentlength > bestlength)
                {
                    if (sublen is not null)
                    {
                        for (int j = bestlength + 1; j <= currentlength; j++)
                            sublen[j] = (ushort)dist;
                    }
                    bestdist = (ushort)dist;
                    bestlength = currentlength;
                    if (currentlength >= limit) break;
                }
            }

            // Cambia a la tabla hash "same" cuando la mejor longitud ya es larga.
            if (!ReferenceEquals(hhead, h.Head2) && bestlength >= h.Same[hpos] && h.Val2 == h.HashVal2[p])
            {
                hhead = h.Head2;
                hprev = h.Prev2;
                hval = h.Val2;
            }

            pp = p;
            p = hprev[p];
            if (p == pp) break;

            dist += p < pp ? pp - p : ((Z.WindowSize - p) + pp);

            chainCounter--;
            if (chainCounter <= 0) break;
        }

        StoreInLongestMatchCache(s, pos, limit, sublen, bestdist, bestlength);

        distance = bestdist;
        length = bestlength;
    }

    public static void Greedy(BlockState s, byte[] input, int instart, int inend, LZ77Store store, ZHash h)
    {
        int windowstart = instart > Z.WindowSize ? instart - Z.WindowSize : 0;
        var dummysublen = new ushort[259];
        int prevLength = 0;
        int prevMatch = 0;
        bool matchAvailable = false;

        if (instart == inend) return;

        h.Reset();
        h.Warmup(input, windowstart, inend);
        for (int i = windowstart; i < instart; i++)
            h.Update(input, i, inend);

        for (int i = instart; i < inend; i++)
        {
            h.Update(input, i, inend);
            FindLongestMatch(s, h, input, i, inend, Z.MaxMatch, dummysublen, out ushort dist, out ushort leng);
            int lengthscore = GetLengthScore(leng, dist);

            // Lazy matching
            int prevlengthscore = GetLengthScore(prevLength, prevMatch);
            if (matchAvailable)
            {
                matchAvailable = false;
                if (lengthscore > prevlengthscore + 1)
                {
                    store.StoreLitLenDist(input[i - 1], 0, i - 1);
                    if (lengthscore >= Z.MinMatch && leng < Z.MaxMatch)
                    {
                        matchAvailable = true;
                        prevLength = leng;
                        prevMatch = dist;
                        continue;
                    }
                }
                else
                {
                    // Añade la coincidencia anterior.
                    leng = (ushort)prevLength;
                    dist = (ushort)prevMatch;
                    store.StoreLitLenDist(leng, dist, i - 1);
                    for (int j = 2; j < leng; j++)
                    {
                        i++;
                        h.Update(input, i, inend);
                    }
                    continue;
                }
            }
            else if (lengthscore >= Z.MinMatch && leng < Z.MaxMatch)
            {
                matchAvailable = true;
                prevLength = leng;
                prevMatch = dist;
                continue;
            }

            if (lengthscore >= Z.MinMatch)
            {
                store.StoreLitLenDist(leng, dist, i);
            }
            else
            {
                leng = 1;
                store.StoreLitLenDist(input[i], 0, i);
            }
            for (int j = 1; j < leng; j++)
            {
                i++;
                h.Update(input, i, inend);
            }
        }
    }
}

// Port a C# del compresor de zlib 1.3 (deflate.c + trees.c), Copyright (C) 1995-2023 Jean-loup Gailly y
// Mark Adler, licencia zlib (ver THIRD_PARTY_NOTICES.md). Solo el camino "deflate_slow" (niveles 4–9) con
// estrategia por defecto, memLevel 8 y ventana de 15 bits, que es lo que usó patch_panel.py:
//   zlib.compressobj(9, zlib.DEFLATED, -15, 8, zlib.Z_DEFAULT_STRATEGY)
// La salida debe ser IDÉNTICA byte a byte a la de zlib 1.3 (lo comprueban las pruebas).
namespace FirmReaper.Core.Compression;

/// <summary>Deflate crudo (RFC 1951) idéntico a zlib 1.3 con nivel 4–9, memLevel 8, ventana 15 bits.</summary>
public static class ZlibDeflater
{
    public static byte[] Compress(ReadOnlySpan<byte> input, int level = 9)
    {
        if (level is < 4 or > 9)
            throw new ArgumentOutOfRangeException(nameof(level), "Solo niveles 4–9 (deflate_slow).");
        var s = new State(input.ToArray(), level);
        s.DeflateSlow();
        return s.Output();
    }

    // ------------------------------------------------------------------ constantes (zlib.h, deflate.h, trees.c)
    private const int MinMatch = 3, MaxMatch = 258;
    private const int WBits = 15, WSize = 1 << WBits, WMask = WSize - 1;
    private const int MemLevel = 8;
    private const int HashBits = MemLevel + 7, HashSize = 1 << HashBits, HashMask = HashSize - 1;
    private const int HashShift = (HashBits + MinMatch - 1) / MinMatch;
    private const int MinLookahead = MaxMatch + MinMatch + 1;
    private const int MaxDist = WSize - MinLookahead;
    private const int WinInit = MaxMatch;
    private const int TooFar = 4096;
    private const int LitBufSize = 1 << (MemLevel + 6);
    private const int SymEnd = LitBufSize - 1; // en símbolos (sym_end = (lit_bufsize-1)*3 bytes)

    private const int LengthCodes = 29, Literals = 256, LCodes = Literals + 1 + LengthCodes, DCodes = 30, BlCodes = 19;
    private const int HeapSize = 2 * LCodes + 1, MaxBits = 15, MaxBlBits = 7, EndBlock = 256;
    private const int Rep3_6 = 16, RepZ3_10 = 17, RepZ11_138 = 18;
    private const int StoredBlock = 0, StaticTrees = 1, DynTrees = 2;

    // good_length, max_lazy, nice_length, max_chain
    private static readonly (int Good, int Lazy, int Nice, int Chain)[] Config =
    [
        (0, 0, 0, 0), (4, 4, 8, 4), (4, 5, 16, 8), (4, 6, 32, 32),
        (4, 4, 16, 16), (8, 16, 32, 32), (8, 16, 128, 128), (8, 32, 128, 256), (32, 128, 258, 1024), (32, 258, 258, 4096),
    ];

    private static readonly int[] ExtraLBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] ExtraDBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly int[] ExtraBlBits = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 7];
    private static readonly byte[] BlOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    // ------------------------------------------------------------------ tablas estáticas (tr_static_init)
    private static readonly byte[] LengthCode = new byte[MaxMatch - MinMatch + 1];
    private static readonly byte[] DistCode = new byte[512];
    private static readonly int[] BaseLength = new int[LengthCodes];
    private static readonly int[] BaseDist = new int[DCodes];
    private static readonly ushort[] StaticLCode = new ushort[LCodes + 2];
    private static readonly ushort[] StaticLLen = new ushort[LCodes + 2];
    private static readonly ushort[] StaticDCode = new ushort[DCodes];
    private static readonly ushort[] StaticDLen = new ushort[DCodes];

    static ZlibDeflater()
    {
        int length = 0, code;
        for (code = 0; code < LengthCodes - 1; code++)
        {
            BaseLength[code] = length;
            for (int n = 0; n < 1 << ExtraLBits[code]; n++) LengthCode[length++] = (byte)code;
        }
        LengthCode[length - 1] = (byte)code; // longitud 258 → código 28
        int dist = 0;
        for (code = 0; code < 16; code++)
        {
            BaseDist[code] = dist;
            for (int n = 0; n < 1 << ExtraDBits[code]; n++) DistCode[dist++] = (byte)code;
        }
        dist >>= 7;
        for (; code < DCodes; code++)
        {
            BaseDist[code] = dist << 7;
            for (int n = 0; n < 1 << (ExtraDBits[code] - 7); n++) DistCode[256 + dist++] = (byte)code;
        }
        var blCount = new ushort[MaxBits + 1];
        int k = 0;
        while (k <= 143) { StaticLLen[k++] = 8; blCount[8]++; }
        while (k <= 255) { StaticLLen[k++] = 9; blCount[9]++; }
        while (k <= 279) { StaticLLen[k++] = 7; blCount[7]++; }
        while (k <= 287) { StaticLLen[k++] = 8; blCount[8]++; }
        GenCodes(StaticLCode, StaticLLen, LCodes + 1, blCount);
        for (int n = 0; n < DCodes; n++)
        {
            StaticDLen[n] = 5;
            StaticDCode[n] = (ushort)BiReverse((uint)n, 5);
        }
    }

    private static int DCode(int dist) => dist < 256 ? DistCode[dist] : DistCode[256 + (dist >> 7)];

    private static uint BiReverse(uint code, int len)
    {
        uint res = 0;
        do
        {
            res |= code & 1;
            code >>= 1;
            res <<= 1;
        } while (--len > 0);
        return res >> 1;
    }

    /// <summary>gen_codes: <paramref name="fc"/> recibe los códigos (comparte memoria con Freq en zlib).</summary>
    private static void GenCodes(ushort[] fc, ushort[] dl, int maxCode, ushort[] blCount)
    {
        var nextCode = new ushort[MaxBits + 1];
        uint code = 0;
        for (int bits = 1; bits <= MaxBits; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = (ushort)code;
        }
        for (int n = 0; n <= maxCode; n++)
        {
            int len = dl[n];
            if (len == 0) continue;
            fc[n] = (ushort)BiReverse(nextCode[len]++, len);
        }
    }

    /// <summary>
    /// Árbol de zlib (ct_data): en C, Freq/Code comparten memoria (fc) y Dad/Len también (dl).
    /// Se reproduce con dos arrays porque el algoritmo depende de esa superposición.
    /// </summary>
    private sealed class Tree(int size, int elems, int maxLength, int[] extra, int extraBase, ushort[]? staticLen)
    {
        public readonly ushort[] Fc = new ushort[size];
        public readonly ushort[] Dl = new ushort[size];
        public readonly int Elems = elems;
        public readonly int MaxLength = maxLength;
        public readonly int[] Extra = extra;
        public readonly int ExtraBase = extraBase;
        public readonly ushort[]? StaticLen = staticLen;
        public int MaxCode;
    }

    private sealed class State
    {
        private readonly byte[] _in;
        private int _inPos;
        private readonly byte[] _window = new byte[2 * WSize];
        private readonly ushort[] _prev = new ushort[WSize];
        private readonly ushort[] _head = new ushort[HashSize];
        private readonly ulong _windowSize = 2UL * WSize;
        private ulong _highWater;
        private uint _insH;
        private long _blockStart;
        private uint _strstart, _matchStart, _lookahead, _insert;
        private uint _prevLength, _matchLength, _prevMatch;
        private bool _matchAvailable;
        private readonly uint _maxLazy, _goodMatch, _maxChain;
        private readonly int _niceMatch;

        // símbolos del bloque (sym_buf)
        private readonly ushort[] _symDist = new ushort[LitBufSize];
        private readonly byte[] _symLc = new byte[LitBufSize];
        private int _symNext;

        private readonly Tree _l = new(HeapSize, LCodes, MaxBits, ExtraLBits, Literals + 1, StaticLLen);
        private readonly Tree _d = new(2 * DCodes + 1, DCodes, MaxBits, ExtraDBits, 0, StaticDLen);
        private readonly Tree _bl = new(2 * BlCodes + 1, BlCodes, MaxBlBits, ExtraBlBits, 0, null);
        private readonly ushort[] _blCount = new ushort[MaxBits + 1];
        private readonly int[] _heap = new int[2 * LCodes + 1];
        private int _heapLen, _heapMax;
        private readonly byte[] _depth = new byte[2 * LCodes + 1];
        private ulong _optLen, _staticLen;

        // salida de bits (LSB primero, como bi_buf)
        private byte[] _out = new byte[1 << 16];
        private int _outLen;
        private ulong _bitBuf;
        private int _bitCount;

        public State(byte[] input, int level)
        {
            _in = input;
            (_goodMatch, _maxLazy, _niceMatch, _maxChain) = ((uint)Config[level].Good, (uint)Config[level].Lazy, Config[level].Nice, (uint)Config[level].Chain);
            _matchLength = _prevLength = MinMatch - 1;
            InitBlock();
        }

        // ---------------------------------------------------------- salida
        private void PutByte(byte b)
        {
            if (_outLen == _out.Length) Array.Resize(ref _out, _out.Length * 2);
            _out[_outLen++] = b;
        }

        private void SendBits(uint value, int length)
        {
            _bitBuf |= (ulong)(value & ((1u << length) - 1)) << _bitCount;
            _bitCount += length;
            while (_bitCount >= 8)
            {
                PutByte((byte)_bitBuf);
                _bitBuf >>= 8;
                _bitCount -= 8;
            }
        }

        private void SendCode(int c, Tree t) => SendBits(t.Fc[c], t.Dl[c]);

        /// <summary>bi_windup: completa el último byte con ceros.</summary>
        private void BiWindup()
        {
            if (_bitCount > 0) PutByte((byte)_bitBuf);
            _bitBuf = 0;
            _bitCount = 0;
        }

        public byte[] Output() => _out.AsSpan(0, _outLen).ToArray();

        // ---------------------------------------------------------- ventana y hash (deflate.c)
        private void UpdateHash(byte c) => _insH = ((_insH << HashShift) ^ c) & HashMask;

        private uint InsertString(uint str)
        {
            UpdateHash(_window[str + MinMatch - 1]);
            uint matchHead = _prev[str & WMask] = _head[_insH];
            _head[_insH] = (ushort)str;
            return matchHead;
        }

        private void SlideHash()
        {
            for (int n = 0; n < HashSize; n++)
            {
                uint m = _head[n];
                _head[n] = (ushort)(m >= WSize ? m - WSize : 0);
            }
            for (int n = 0; n < WSize; n++)
            {
                uint m = _prev[n];
                _prev[n] = (ushort)(m >= WSize ? m - WSize : 0);
            }
        }

        private void FillWindow()
        {
            do
            {
                uint more = (uint)(_windowSize - _lookahead - _strstart);
                if (_strstart >= WSize + MaxDist)
                {
                    Array.Copy(_window, WSize, _window, 0, WSize - more);
                    _matchStart -= WSize;
                    _strstart -= WSize;
                    _blockStart -= WSize;
                    if (_insert > _strstart) _insert = _strstart;
                    SlideHash();
                    more += WSize;
                }
                if (_inPos == _in.Length) break;

                // read_buf
                int n = (int)Math.Min(more, (uint)(_in.Length - _inPos));
                Array.Copy(_in, _inPos, _window, _strstart + _lookahead, n);
                _inPos += n;
                _lookahead += (uint)n;

                if (_lookahead + _insert >= MinMatch)
                {
                    uint str = _strstart - _insert;
                    _insH = _window[str];
                    UpdateHash(_window[str + 1]);
                    while (_insert != 0)
                    {
                        UpdateHash(_window[str + MinMatch - 1]);
                        _prev[str & WMask] = _head[_insH];
                        _head[_insH] = (ushort)str;
                        str++;
                        _insert--;
                        if (_lookahead + _insert < MinMatch) break;
                    }
                }
            } while (_lookahead < MinLookahead && _inPos != _in.Length);

            // Pone a cero lo que hay justo después de los datos (lo puede leer longest_match).
            if (_highWater < _windowSize)
            {
                ulong curr = _strstart + (ulong)_lookahead;
                ulong init;
                if (_highWater < curr)
                {
                    init = _windowSize - curr;
                    if (init > WinInit) init = WinInit;
                    Array.Clear(_window, (int)curr, (int)init);
                    _highWater = curr + init;
                }
                else if (_highWater < curr + WinInit)
                {
                    init = curr + WinInit - _highWater;
                    if (init > _windowSize - _highWater) init = _windowSize - _highWater;
                    Array.Clear(_window, (int)_highWater, (int)init);
                    _highWater += init;
                }
            }
        }

        private uint LongestMatch(uint curMatch)
        {
            uint chainLength = _maxChain;
            uint scan = _strstart;
            int bestLen = (int)_prevLength;
            int niceMatch = _niceMatch;
            uint limit = _strstart > MaxDist ? _strstart - MaxDist : 0;
            uint strend = _strstart + MaxMatch;
            byte scanEnd1 = _window[scan + bestLen - 1];
            byte scanEnd = _window[scan + bestLen];

            if (_prevLength >= _goodMatch) chainLength >>= 2;
            if ((uint)niceMatch > _lookahead) niceMatch = (int)_lookahead;

            do
            {
                uint match = curMatch;
                if (_window[match + bestLen] != scanEnd ||
                    _window[match + bestLen - 1] != scanEnd1 ||
                    _window[match] != _window[scan] ||
                    _window[match + 1] != _window[scan + 1])
                    continue;

                // scan[2] == match[2] está garantizado por el hash: se compara desde el byte 3.
                uint sp = scan + 2, mp = match + 2;
                do
                {
                } while (_window[++sp] == _window[++mp] && sp < strend);
                int len = MaxMatch - (int)(strend - sp);

                if (len > bestLen)
                {
                    _matchStart = curMatch;
                    bestLen = len;
                    if (len >= niceMatch) break;
                    scanEnd1 = _window[scan + bestLen - 1];
                    scanEnd = _window[scan + bestLen];
                }
            } while ((curMatch = _prev[curMatch & WMask]) > limit && --chainLength != 0);

            return (uint)bestLen <= _lookahead ? (uint)bestLen : _lookahead;
        }

        // ---------------------------------------------------------- deflate_slow
        public void DeflateSlow()
        {
            for (; ; )
            {
                if (_lookahead < MinLookahead)
                {
                    FillWindow();
                    if (_lookahead == 0) break;
                }

                uint hashHead = 0;
                if (_lookahead >= MinMatch) hashHead = InsertString(_strstart);

                _prevLength = _matchLength;
                _prevMatch = _matchStart;
                _matchLength = MinMatch - 1;

                if (hashHead != 0 && _prevLength < _maxLazy && _strstart - hashHead <= MaxDist)
                {
                    _matchLength = LongestMatch(hashHead);
                    if (_matchLength <= 5 && _matchLength == MinMatch && _strstart - _matchStart > TooFar)
                        _matchLength = MinMatch - 1;
                }

                if (_prevLength >= MinMatch && _matchLength <= _prevLength)
                {
                    uint maxInsert = _strstart + _lookahead - MinMatch;
                    bool bflush = TallyDist((int)(_strstart - 1 - _prevMatch), (int)(_prevLength - MinMatch));
                    _lookahead -= _prevLength - 1;
                    _prevLength -= 2;
                    do
                    {
                        if (++_strstart <= maxInsert) InsertString(_strstart);
                    } while (--_prevLength != 0);
                    _matchAvailable = false;
                    _matchLength = MinMatch - 1;
                    _strstart++;
                    if (bflush) FlushBlock(false);
                }
                else if (_matchAvailable)
                {
                    if (TallyLit(_window[_strstart - 1])) FlushBlock(false);
                    _strstart++;
                    _lookahead--;
                }
                else
                {
                    _matchAvailable = true;
                    _strstart++;
                    _lookahead--;
                }
            }
            if (_matchAvailable)
            {
                TallyLit(_window[_strstart - 1]);
                _matchAvailable = false;
            }
            _insert = _strstart < MinMatch - 1 ? _strstart : MinMatch - 1;
            FlushBlock(true); // Z_FINISH
        }

        private bool TallyLit(byte c)
        {
            _symDist[_symNext] = 0;
            _symLc[_symNext] = c;
            _symNext++;
            _l.Fc[c]++;
            return _symNext == SymEnd;
        }

        private bool TallyDist(int distance, int length)
        {
            _symDist[_symNext] = (ushort)distance;
            _symLc[_symNext] = (byte)length;
            _symNext++;
            int dist = (ushort)distance - 1;
            _l.Fc[LengthCode[length] + Literals + 1]++;
            _d.Fc[DCode(dist)]++;
            return _symNext == SymEnd;
        }

        private void FlushBlock(bool last)
        {
            int? buf = _blockStart >= 0 ? (int)_blockStart : null;
            FlushBlockTrees(buf, (ulong)((long)_strstart - _blockStart), last);
            _blockStart = _strstart;
        }

        // ---------------------------------------------------------- trees.c
        private void InitBlock()
        {
            for (int n = 0; n < LCodes; n++) _l.Fc[n] = 0;
            for (int n = 0; n < DCodes; n++) _d.Fc[n] = 0;
            for (int n = 0; n < BlCodes; n++) _bl.Fc[n] = 0;
            _l.Fc[EndBlock] = 1;
            _optLen = _staticLen = 0;
            _symNext = 0;
        }

        private static bool Smaller(Tree t, int n, int m, byte[] depth) =>
            t.Fc[n] < t.Fc[m] || (t.Fc[n] == t.Fc[m] && depth[n] <= depth[m]);

        private void PqDownHeap(Tree t, int k)
        {
            int v = _heap[k];
            int j = k << 1;
            while (j <= _heapLen)
            {
                if (j < _heapLen && Smaller(t, _heap[j + 1], _heap[j], _depth)) j++;
                if (Smaller(t, v, _heap[j], _depth)) break;
                _heap[k] = _heap[j];
                k = j;
                j <<= 1;
            }
            _heap[k] = v;
        }

        private void GenBitLen(Tree t)
        {
            int maxCode = t.MaxCode;
            int overflow = 0;
            for (int bits = 0; bits <= MaxBits; bits++) _blCount[bits] = 0;

            // Dad y Len comparten memoria: Len de la raíz = 0.
            t.Dl[_heap[_heapMax]] = 0;
            int h;
            for (h = _heapMax + 1; h < HeapSize; h++)
            {
                int n = _heap[h];
                int bits = t.Dl[t.Dl[n]] + 1; // tree[tree[n].Dad].Len + 1
                if (bits > t.MaxLength) { bits = t.MaxLength; overflow++; }
                t.Dl[n] = (ushort)bits;
                if (n > maxCode) continue; // no es una hoja
                _blCount[bits]++;
                int xbits = n >= t.ExtraBase ? t.Extra[n - t.ExtraBase] : 0;
                ushort f = t.Fc[n];
                _optLen += (ulong)f * (uint)(bits + xbits);
                if (t.StaticLen is not null) _staticLen += (ulong)f * (uint)(t.StaticLen[n] + xbits);
            }
            if (overflow == 0) return;

            do
            {
                int bits = t.MaxLength - 1;
                while (_blCount[bits] == 0) bits--;
                _blCount[bits]--;
                _blCount[bits + 1] += 2;
                _blCount[t.MaxLength]--;
                overflow -= 2;
            } while (overflow > 0);

            for (int bits = t.MaxLength; bits != 0; bits--)
            {
                int n = _blCount[bits];
                while (n != 0)
                {
                    int m = _heap[--h];
                    if (m > maxCode) continue;
                    if (t.Dl[m] != bits)
                    {
                        _optLen += unchecked(((ulong)bits - t.Dl[m]) * t.Fc[m]);
                        t.Dl[m] = (ushort)bits;
                    }
                    n--;
                }
            }
        }

        private void BuildTree(Tree t)
        {
            int maxCode = -1;
            _heapLen = 0;
            _heapMax = HeapSize;
            for (int n = 0; n < t.Elems; n++)
            {
                if (t.Fc[n] != 0)
                {
                    _heap[++_heapLen] = maxCode = n;
                    _depth[n] = 0;
                }
                else
                {
                    t.Dl[n] = 0;
                }
            }
            // Al menos dos códigos (para que el árbol sea válido).
            while (_heapLen < 2)
            {
                int node = _heap[++_heapLen] = maxCode < 2 ? ++maxCode : 0;
                t.Fc[node] = 1;
                _depth[node] = 0;
                _optLen = unchecked(_optLen - 1);
                if (t.StaticLen is not null) _staticLen = unchecked(_staticLen - t.StaticLen[node]);
            }
            t.MaxCode = maxCode;

            for (int n = _heapLen / 2; n >= 1; n--) PqDownHeap(t, n);

            int nodeId = t.Elems;
            do
            {
                // pqremove
                int n = _heap[1];
                _heap[1] = _heap[_heapLen--];
                PqDownHeap(t, 1);
                int m = _heap[1];

                _heap[--_heapMax] = n;
                _heap[--_heapMax] = m;

                t.Fc[nodeId] = (ushort)(t.Fc[n] + t.Fc[m]);
                _depth[nodeId] = (byte)((_depth[n] >= _depth[m] ? _depth[n] : _depth[m]) + 1);
                t.Dl[n] = t.Dl[m] = (ushort)nodeId;

                _heap[1] = nodeId++;
                PqDownHeap(t, 1);
            } while (_heapLen >= 2);

            _heap[--_heapMax] = _heap[1];

            GenBitLen(t);
            GenCodes(t.Fc, t.Dl, maxCode, _blCount);
        }

        private void ScanTree(Tree t, int maxCode)
        {
            int prevlen = -1, nextlen = t.Dl[0], count = 0, maxCount = 7, minCount = 4;
            if (nextlen == 0) { maxCount = 138; minCount = 3; }
            t.Dl[maxCode + 1] = 0xffff; // centinela
            for (int n = 0; n <= maxCode; n++)
            {
                int curlen = nextlen;
                nextlen = t.Dl[n + 1];
                if (++count < maxCount && curlen == nextlen) continue;
                else if (count < minCount) _bl.Fc[curlen] += (ushort)count;
                else if (curlen != 0)
                {
                    if (curlen != prevlen) _bl.Fc[curlen]++;
                    _bl.Fc[Rep3_6]++;
                }
                else if (count <= 10) _bl.Fc[RepZ3_10]++;
                else _bl.Fc[RepZ11_138]++;
                count = 0;
                prevlen = curlen;
                if (nextlen == 0) { maxCount = 138; minCount = 3; }
                else if (curlen == nextlen) { maxCount = 6; minCount = 3; }
                else { maxCount = 7; minCount = 4; }
            }
        }

        private void SendTree(Tree t, int maxCode)
        {
            int prevlen = -1, nextlen = t.Dl[0], count = 0, maxCount = 7, minCount = 4;
            if (nextlen == 0) { maxCount = 138; minCount = 3; }
            for (int n = 0; n <= maxCode; n++)
            {
                int curlen = nextlen;
                nextlen = t.Dl[n + 1];
                if (++count < maxCount && curlen == nextlen) continue;
                else if (count < minCount)
                {
                    do { SendCode(curlen, _bl); } while (--count != 0);
                }
                else if (curlen != 0)
                {
                    if (curlen != prevlen)
                    {
                        SendCode(curlen, _bl);
                        count--;
                    }
                    SendCode(Rep3_6, _bl);
                    SendBits((uint)(count - 3), 2);
                }
                else if (count <= 10)
                {
                    SendCode(RepZ3_10, _bl);
                    SendBits((uint)(count - 3), 3);
                }
                else
                {
                    SendCode(RepZ11_138, _bl);
                    SendBits((uint)(count - 11), 7);
                }
                count = 0;
                prevlen = curlen;
                if (nextlen == 0) { maxCount = 138; minCount = 3; }
                else if (curlen == nextlen) { maxCount = 6; minCount = 3; }
                else { maxCount = 7; minCount = 4; }
            }
        }

        private int BuildBlTree()
        {
            ScanTree(_l, _l.MaxCode);
            ScanTree(_d, _d.MaxCode);
            BuildTree(_bl);
            int maxBlIndex;
            for (maxBlIndex = BlCodes - 1; maxBlIndex >= 3; maxBlIndex--)
                if (_bl.Dl[BlOrder[maxBlIndex]] != 0) break;
            _optLen += 3 * ((ulong)maxBlIndex + 1) + 5 + 5 + 4;
            return maxBlIndex;
        }

        private void SendAllTrees(int lcodes, int dcodes, int blcodes)
        {
            SendBits((uint)(lcodes - 257), 5);
            SendBits((uint)(dcodes - 1), 5);
            SendBits((uint)(blcodes - 4), 4);
            for (int rank = 0; rank < blcodes; rank++)
                SendBits(_bl.Dl[BlOrder[rank]], 3);
            SendTree(_l, lcodes - 1);
            SendTree(_d, dcodes - 1);
        }

        private void SendStoredBlock(int buf, ulong storedLen, bool last)
        {
            SendBits((uint)((StoredBlock << 1) + (last ? 1 : 0)), 3);
            BiWindup();
            PutByte((byte)storedLen);
            PutByte((byte)(storedLen >> 8));
            ushort nlen = (ushort)~storedLen;
            PutByte((byte)nlen);
            PutByte((byte)(nlen >> 8));
            for (ulong i = 0; i < storedLen; i++) PutByte(_window[buf + (int)i]);
        }

        private void CompressBlock(ushort[] lCode, ushort[] lLen, ushort[] dCode, ushort[] dLen)
        {
            for (int sx = 0; sx < _symNext; sx++)
            {
                int dist = _symDist[sx];
                int lc = _symLc[sx];
                if (dist == 0)
                {
                    SendBits(lCode[lc], lLen[lc]);
                }
                else
                {
                    int code = LengthCode[lc];
                    SendBits(lCode[code + Literals + 1], lLen[code + Literals + 1]);
                    int extra = ExtraLBits[code];
                    if (extra != 0) SendBits((uint)(lc - BaseLength[code]), extra);
                    dist--;
                    code = DCode(dist);
                    SendBits(dCode[code], dLen[code]);
                    extra = ExtraDBits[code];
                    if (extra != 0) SendBits((uint)(dist - BaseDist[code]), extra);
                }
            }
            SendBits(lCode[EndBlock], lLen[EndBlock]);
        }

        private void FlushBlockTrees(int? buf, ulong storedLen, bool last)
        {
            BuildTree(_l);
            BuildTree(_d);
            int maxBlIndex = BuildBlTree();
            ulong optLenb = (_optLen + 3 + 7) >> 3;
            ulong staticLenb = (_staticLen + 3 + 7) >> 3;
            if (staticLenb <= optLenb) optLenb = staticLenb;

            if (storedLen + 4 <= optLenb && buf is not null)
            {
                SendStoredBlock(buf.Value, storedLen, last);
            }
            else if (staticLenb == optLenb)
            {
                SendBits((uint)((StaticTrees << 1) + (last ? 1 : 0)), 3);
                CompressBlock(StaticLCode, StaticLLen, StaticDCode, StaticDLen);
            }
            else
            {
                SendBits((uint)((DynTrees << 1) + (last ? 1 : 0)), 3);
                SendAllTrees(_l.MaxCode + 1, _d.MaxCode + 1, maxBlIndex + 1);
                CompressBlock(_l.Fc, _l.Dl, _d.Fc, _d.Dl);
            }
            InitBlock();
            if (last) BiWindup();
        }
    }
}

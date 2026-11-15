#!/usr/bin/env python3
"""Mini desensamblador AEON R2 (MStar) guiado por las tablas oficiales del
ensamblador (isa/aeon_isa.json del proyecto ghidra-plugin-aeon).

- Instrucciones big-endian de 2, 3 o 4 bytes (bits altos del primer byte:
  100 = 2, 0xx = 3, 101/11x = 4).
- Decodifica por la primera entrada que coincide (como objdump).
- Operandos: signo, escala (shift), sesgo (add) y relativo al PC, según 'letters'.
"""
import json
import sys

ISA = __import__('os').path.join(__import__('os').path.dirname(__import__('os').path.abspath(__file__)), 'aeon_isa.json')


def ilen(b0):
    t = b0 >> 5
    if t == 0b100:
        return 2
    if t < 0b100:
        return 3
    return 4


class Dis:
    def __init__(self):
        isa = json.load(open(ISA))['aeon_aeonR2_isa']
        self.letters = {l['letter']: l for l in isa['letters'] if l['letter']}
        self.tab = {2: [], 3: [], 4: []}
        for op in isa['opcodes']:
            if op['for_encode_only']:
                continue
            enc = op['encoding'].replace(' ', '')
            n = len(enc)
            if n not in (16, 24, 32):
                continue
            mask = val = 0
            pos = {}
            for i, c in enumerate(enc):
                bit = n - 1 - i
                if c in '01':
                    mask |= 1 << bit
                    if c == '1':
                        val |= 1 << bit
                elif c != '-':
                    pos.setdefault(c, []).append(bit)
            self.tab[n // 8].append((mask, val, op['name'], op['args'], pos))

    def operand(self, ch, word, pos, pc):
        L = self.letters.get(ch)
        bits = pos[ch]                  # MSB first
        v = 0
        for b in bits:
            v = (v << 1) | ((word >> b) & 1)
        if L is None:
            return v, False
        if L['flags'] & 2:              # signed
            w = len(bits)
            if v & (1 << (w - 1)):
                v -= 1 << w
        v = (v << L['shift']) + L['add']
        if L['flags'] & 8:              # pc relative
            v = (pc + v) & 0xFFFFFFFF
        return v, bool(L['flags'] & 1)

    def decode(self, buf, off, pc):
        n = ilen(buf[off])
        if off + n > len(buf):
            return None
        word = int.from_bytes(buf[off:off + n], 'big')
        for mask, val, name, args, pos in self.tab[n]:
            if word & mask == val:
                ops = {}
                for ch in pos:
                    ops[ch] = self.operand(ch, word, pos, pc)
                return n, name, args, ops
        return n, None, None, None


def fmt(name, args, ops):
    if name is None:
        return '.word'
    out = args
    for ch, (v, isreg) in ops.items():
        s = ('r%d' % v) if isreg else (hex(v) if v >= 0 else '-' + hex(-v))
        out = out.replace('r' + ch, s, 1) if ('r' + ch) in out else out.replace(ch, s, 1)
    return '%-12s %s' % (name, out)


def sweep(buf, base, start=0, end=None):
    d = Dis()
    end = end or len(buf)
    off = start
    res = []
    while off < end:
        r = d.decode(buf, off, base + off)
        if r is None:
            break
        n, name, args, ops = r
        res.append((base + off, n, name, args, ops))
        off += n
    return res


if __name__ == '__main__':
    buf = open(sys.argv[1], 'rb').read()
    base = int(sys.argv[2], 16)
    a, b = int(sys.argv[3], 16), int(sys.argv[4], 16)
    for pc, n, name, args, ops in sweep(buf, base, a - base, b - base):
        print('%08x  %-10s %s' % (pc, buf[pc - base:pc - base + n].hex(), fmt(name, args, ops)))

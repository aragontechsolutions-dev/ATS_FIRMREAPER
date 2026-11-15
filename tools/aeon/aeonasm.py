#!/usr/bin/env python3
"""Mini ensamblador AEON R2 (inverso de aeondis.py), con las tablas oficiales.
asm(nombre, {letra: valor}, pc) -> bytes. Cada instrucción se decodifica de
vuelta y se compara, así que un error de codificación es imposible de pasar
por alto."""
import json
import sys
from aeondis import Dis, ISA

_isa = json.load(open(ISA))['aeon_aeonR2_isa']
_letters = {l['letter']: l for l in _isa['letters'] if l['letter']}
_ops = {}
for op in _isa['opcodes']:
    if op['for_encode_only']:
        continue
    _ops.setdefault(op['name'], []).append(op)
_dis = Dis()


def asm(name, vals, pc):
    last = None
    for op in _ops[name]:
        enc = op['encoding'].replace(' ', '')
        n = len(enc)
        if n not in (16, 24, 32):
            continue
        pos = {}
        word = 0
        for i, c in enumerate(enc):
            bit = n - 1 - i
            if c == '1':
                word |= 1 << bit
            elif c in '0-':
                pass
            else:
                pos.setdefault(c, []).append(bit)
        if set(pos) != set(vals):
            last = 'letras %s != %s' % (sorted(pos), sorted(vals))
            continue
        ok = True
        for ch, v in vals.items():
            L = _letters.get(ch)
            bits = pos[ch]
            w = len(bits)
            x = v
            if L is not None:
                if L['flags'] & 8:
                    x = v - pc
                x -= L['add']
                if x & ((1 << L['shift']) - 1):
                    ok = False
                    break
                x >>= L['shift']
                if L['flags'] & 2:
                    if not -(1 << (w - 1)) <= x < (1 << (w - 1)):
                        ok = False
                        break
                    x &= (1 << w) - 1
                elif not 0 <= x < (1 << w):
                    ok = False
                    break
            for k, b in enumerate(bits):
                if (x >> (w - 1 - k)) & 1:
                    word |= 1 << b
        if not ok:
            last = 'fuera de rango'
            continue
        out = word.to_bytes(n // 8, 'big')
        r = _dis.decode(out, 0, pc)
        if r[1] != name:
            last = 'decodifica como %s' % r[1]
            continue
        got = {k: v for k, (v, isr) in r[3].items()}
        if got != vals:
            last = 'valores %s != %s' % (got, vals)
            continue
        return out
    raise ValueError('no se pudo ensamblar %s %s @%x: %s' % (name, vals, pc, last))


def show_letters(name):
    for op in _ops[name]:
        print(name, op['encoding'], op['args'])


if __name__ == '__main__':
    for nm in sys.argv[1:]:
        show_letters(nm)

#!/usr/bin/env python3
"""RCA (BD_V59S_PB813) + KT1440 código 005: prueba del botón ►II en el reproductor USB.

Mismo gancho que la v3 (0x275411: bg.jal 0x284EDE -> rutina) y mismo hueco (0x341112,
textos de depuración JPEG/MPO sin referencias). La tabla de teclas pone ►II (código 47)
en la tecla 0x73, que 0x2850D3 traduce a VK 0x3D (PLAY/PAUSA).

Variante E1 ("reproducir"): en el reproductor USB (subestado 0x3D3768 == 0x12), si el VK
es 0x3D y no hay reproducción en curso (bandera 0x3D0460 & 4 a cero, la misma que
comprueba la tecla rápida VK_PLAY_PAUSE en 0x2A3A4B), llama al ejecutor de acciones
0x29E6A2 con 0x310 (EN_EXE_DMP_FILE_PAGE_PLAYBACK) y devuelve 1 (tecla consumida, como
hacen las teclas rápidas). Con reproducción en curso sigue como el original (pausa).

Variante E2 ("diagnóstico"): mismas condiciones, pero cambia el VK del mensaje por 0x12
(el de la flecha ABAJO de la cruceta, que se mueve en la lista). Si el cursor baja al
pulsar ►II, la tecla llega a la interfaz; si no, se pierde antes.

Uso: build_cave_rca_play.py E1|E2  -> cave_rca_play_E1.json / _E2.json
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from aeonasm import asm
from aeondis import Dis, fmt

CAVE = 0x341112
CAVE_END = 0x341190
HOOK = 0x275411
F_HOTKEY = 0x284EDE
F_ACCION = 0x29E6A2
SUBSTATE_DMP = 0x12
VK_PLAY_PAUSE = 0x3D
ACC_PLAYBACK = 0x310
VK_ABAJO = 0x12

variante = sys.argv[1] if len(sys.argv) > 1 else 'E1'
prog = [
    (None, 'bg.movhi', {'D': 7, 'U': 0x3D}),
    (None, 'bg.lwz', {'D': 7, 'A': 7, 'W': 0x3768}),
    (None, 'bn.sfeqi', {'A': 7, 'O': SUBSTATE_DMP}),
    (None, 'bn.bnf', lambda l: {'~': l['GO']}),
    (None, 'bn.sfeqi', {'A': 3, 'O': VK_PLAY_PAUSE}),
    (None, 'bn.bnf', lambda l: {'~': l['GO']}),
    (None, 'bg.movhi', {'D': 7, 'U': 0x3D}),
    (None, 'bg.lwz', {'D': 7, 'A': 7, 'W': 0x460}),
    (None, 'bn.andi', {'D': 7, 'A': 7, 'N': 4}),
    (None, 'bn.sfeqi', {'A': 7, 'O': 0}),
    (None, 'bn.bnf', lambda l: {'~': l['GO']}),
]
if variante == 'E1':
    prog += [
        (None, 'bn.addi', {'D': 1, 'A': 1, 'O': -4}),
        (None, 'bn.sw', {':': 0, 'A': 1, 'B': 9}),
        (None, 'bg.ori', {'D': 3, 'A': 0, 't': ACC_PLAYBACK}),
        (None, 'bg.jal', {'/': F_ACCION}),
        (None, 'bn.lwz', {'D': 9, 'A': 1, ':': 0}),
        (None, 'bn.addi', {'D': 1, 'A': 1, 'O': 4}),
        (None, 'bt.movi', {'D': 3, 'I': 1}),
        (None, 'bt.jr', {'A': 9}),
    ]
elif variante == 'E2':
    prog += [
        (None, 'bn.ori', {'D': 3, 'A': 0, 'N': VK_ABAJO}),
        (None, 'bn.sw', {':': 8, 'A': 10, 'B': 3}),
    ]
else:
    raise SystemExit('variante E1 o E2')
prog.append(('GO', 'bg.j', {'/': F_HOTKEY}))

labels = {'GO': CAVE}
for _ in range(3):
    pc = CAVE
    out = b''
    lst = []
    for lab, name, vals in prog:
        if lab:
            labels[lab] = pc
        v = vals(labels) if callable(vals) else vals
        b = asm(name, v, pc)
        lst.append((pc, b))
        out += b
        pc += len(b)
assert CAVE + len(out) <= CAVE_END, 'la rutina no cabe'
d = Dis()
for pc, b in lst:
    r = d.decode(b, 0, pc)
    print('  %06x %-10s %s' % (pc, b.hex(), fmt(r[1], r[2], r[3])))
hook = asm('bg.jal', {'/': CAVE}, HOOK)
orig = asm('bg.jal', {'/': F_HOTKEY}, HOOK)
print('gancho %x: %s -> %s; rutina %d bytes' % (HOOK, orig.hex(), hook.hex(), len(out)))
json.dump({'CAVE': CAVE, 'code': out.hex(), 'HOOK': HOOK, 'hook': hook.hex(),
           'hook_original': orig.hex()}, open('cave_rca_play_%s.json' % variante, 'w'), indent=1)

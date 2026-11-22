#!/usr/bin/env python3
"""RCA (BD_MST030B_10AL8_12052) + mando Soyea, prueba v3: en el reproductor USB
la cruceta del Soyea (que envía VOL+/VOL-/CH+/CH-) se convierte en flechas.

Cómo funciona (direcciones = RAM de la app, ver docs/historial/README_RCA_MANDO_SOYEA.md):
- 0x2850D3 traduce la tecla a la tecla virtual (VK) de la interfaz ZUI. Con la tabla
  del Soyea de la v2: VOL+ (0x12) -> VK 0x27, VOL- (0x13) -> VK 0x0C,
  CH+ (0x14) -> VK 0x0D, CH- (0x15) -> VK 0x05.
- Navegación ZUI (0x2729DF): VK 0x0E arriba, 0x12 abajo, 0x13 izquierda, 0x15 derecha.
- 0x2753D3 recibe el mensaje de tecla (r10 = mensaje, VK en +8) y, en el estado ZUI 2,
  llama en 0x275411 a 0x284EDE (teclas rápidas según el subestado 0x3D3768;
  el subestado 0x12 es el reproductor USB, 0x2A3619).
- El gancho cambia esa llamada por la rutina; si el subestado es 0x12 y el VK es uno de
  los cuatro de la cruceta, reescribe el VK del mensaje con la flecha y sigue en
  0x284EDE como el original. Usa solo r3 y r7 (0x284EDE machaca r7 al entrar).

La rutina va en 0x341112: textos de depuración del decodificador JPEG/MPO sin ninguna
referencia en el código ("Progressive image size too big...", "APP2",
"_pu8In_buf_ofs = %lx", "num of entry = %d"). Salida: cave_rca_v3.json
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from aeonasm import asm
from aeondis import Dis, fmt

CAVE = 0x341112
CAVE_END = 0x341190        # antes de "\x1b[1;31m" (0x341194, sí se usa)
HOOK = 0x275411            # bg.jal 0x284EDE
F_HOTKEY = 0x284EDE
SUBSTATE_DMP = 0x12
MAPA = [(0x27, 0x15),      # VOL+ del Soyea -> DERECHA
        (0x0C, 0x13),      # VOL-           -> IZQUIERDA
        (0x0D, 0x0E),      # CH+            -> ARRIBA
        (0x05, 0x12)]      # CH-            -> ABAJO

prog = [
    (None, 'bg.movhi', {'D': 7, 'U': 0x3D}),
    (None, 'bg.lwz', {'D': 7, 'A': 7, 'W': 0x3768}),
    (None, 'bn.sfeqi', {'A': 7, 'O': SUBSTATE_DMP}),
    (None, 'bn.bnf', lambda l: {'~': l['GO']}),
]
for n, (vk, flecha) in enumerate(MAPA):
    prog += [
        ('N%d' % n, 'bn.sfeqi', {'A': 3, 'O': vk}),
        (None, 'bn.bnf', lambda l, n=n: {'~': l['N%d' % (n + 1)] if n < 3 else l['GO']}),
        (None, 'bn.ori', {'D': 3, 'A': 0, 'N': flecha}),
        (None, 'bn.sw', {':': 8, 'A': 10, 'B': 3}),
    ]
    if n < 3:
        prog.append((None, 'bg.j', lambda l: {'/': l['GO']}))
prog.append(('GO', 'bg.j', {'/': F_HOTKEY}))

labels = {k: CAVE for k in ('GO', 'N1', 'N2', 'N3')}
for _ in range(4):
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
r = d.decode(hook, 0, HOOK)
print('gancho %x: %s -> %s %s' % (HOOK, orig.hex(), hook.hex(), fmt(r[1], r[2], r[3])))
print('rutina: %d bytes (hueco %d)' % (len(out), CAVE_END - CAVE))
json.dump({'CAVE': CAVE, 'code': out.hex(), 'HOOK': HOOK, 'hook': hook.hex(),
           'hook_original': orig.hex()}, open('cave_rca_v3.json', 'w'), indent=1)

#!/usr/bin/env python3
"""Genera la rutina del gancho v2 (flechas/VOL/CH del Soyea) con el
ensamblador AEON hecho a partir de las tablas oficiales, y la verifica
desensamblándola. Salida: cave_v2.json"""
import json, sys
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
from aeonasm import asm
from aeondis import Dis, fmt

CAVE = 0x3BB400
F_MAP = 0x303D2E      # código del mando -> índice (+4 de la estructura de tecla)
F_OSD = 0x2E5A7E      # devuelve el OSD activo (0x521E60)
F_MENU = 0x303BA8     # ¿OSD navegable? (la usa el teclado frontal)
F_PLAYING = 0x278DC1  # 1 si hay reproducción multimedia en curso (banderas 0x3C0)
F_VISIBLE = 0x2C5152  # ¿ventana ZUI visible?
HWND_BROWSER = 0x129  # explorador de archivos USB (rejilla de 7 columnas en 0x2ED00E)
HOOK = 0x2B4A60       # jal 0x303D2E dentro de la rutina de teclas 0x2B49F5


def hi(x): return (x + 0x8000) >> 16
def lo(x): return x - (hi(x) << 16)


prog = [
    (None, 'bn.addi', {'D': 1, 'A': 1, 'O': -8}),
    (None, 'bn.sw', {':': 4, 'A': 1, 'B': 9}),
    (None, 'bn.sw', {':': 0, 'A': 1, 'B': 10}),
    (None, 'bt.mov', {'D': 10, 'A': 3}),                 # r10 = estructura de tecla
    (None, 'bg.jal', {'/': F_MAP}),                      # rutina original
    (None, 'bn.lbz', {'?': 7, 'A': 10, 'z': 0}),
    (None, 'bn.andi', {'D': 7, 'A': 7, 'N': 0x18}),
    (None, 'bg.bnei', lambda l: {'A': 7, 'I': 8, ';': l['END']}),     # solo mando 1
    (None, 'bn.lhz', {'D': 7, 'A': 10, '_': 4}),
    (None, 'bn.addi', {'D': 7, 'A': 7, 'O': -0x12}),
    (None, 'bg.bgtui', lambda l: {'A': 7, 'I': 3, ';': l['END']}),    # solo índices 12..15
    (None, 'bg.jal', {'/': F_OSD}),
    (None, 'bg.beqi', lambda l: {'A': 3, 'I': 7, ';': l['CONV']}),
    (None, 'bn.sfeqi', {'A': 3, 'O': 0x10}),                            # OSD 0x10 = reproductor USB
    (None, 'bn.bnf', lambda l: {'~': l['MENU']}),
    (None, 'bg.jal', {'/': F_PLAYING}),                                  # ¿se reproduce algo?
    (None, 'bg.beqi', lambda l: {'A': 3, 'I': 0, ';': l['CONV']}),      #     no -> flechas
    (None, 'bg.ori', {'D': 3, 'A': 0, 't': HWND_BROWSER}),
    (None, 'bg.jal', {'/': F_VISIBLE}),                                  #     ¿explorador visible?
    (None, 'bg.bnei', lambda l: {'A': 3, 'I': 0, ';': l['CONV']}),      #     sí -> flechas
    (None, 'bn.lhz', {'D': 7, 'A': 10, '_': 4}),                        #     reproducción a pantalla completa (v5):
    (None, 'bn.addi', {'D': 7, 'A': 7, 'O': -0x12}),
    (None, 'bg.bleui', lambda l: {'A': 7, 'I': 1, ';': l['END']}),      #       VOL+/VOL- (12,13) -> volumen
    (None, 'bg.j', lambda l: {'/': l['CONV']}),                          #       CH+/CH- (14,15) -> ARRIBA/ABAJO
    ('MENU', 'bg.jal', {'/': F_MENU}),
    (None, 'bg.beqi', lambda l: {'A': 3, 'I': 0, ';': l['END']}),
    ('CONV', 'bn.lhz', {'D': 7, 'A': 10, '_': 4}),
    (None, 'bg.movhi', lambda l: {'D': 6, 'U': hi(l['TBL'] - 0x12)}),
    (None, 'bg.addi', lambda l: {'D': 6, 'A': 6, 'Y': lo(l['TBL'] - 0x12)}),
    (None, 'bt.add', {'D': 6, 'A': 7}),
    (None, 'bn.lbz', {'?': 7, 'A': 6, 'z': 0}),
    (None, 'bn.sh', {'_': 4, 'A': 10, 'B': 7}),
    ('END', 'bn.lwz', {'D': 10, 'A': 1, ':': 0}),
    (None, 'bn.lwz', {'D': 9, 'A': 1, ':': 4}),
    (None, 'bn.addi', {'D': 1, 'A': 1, 'O': 8}),
    (None, 'bt.jr', {'A': 9}),
]
TBL4 = bytes([0x10, 0x0F, 0x0D, 0x0E])   # VOL+ -> DERECHA, VOL- -> IZQUIERDA, CH+ -> ARRIBA, CH- -> ABAJO

labels = {'END': CAVE, 'CONV': CAVE, 'TBL': CAVE, 'MENU': CAVE}
for _ in range(3):
    pc = CAVE; out = b''; lst = []
    for lab, name, vals in prog:
        if lab: labels[lab] = pc
        v = vals(labels) if callable(vals) else vals
        b = asm(name, v, pc); lst.append((pc, b)); out += b; pc += len(b)
    labels['TBL'] = pc
code = out + TBL4
d = Dis()
for pc, b in lst:
    r = d.decode(b, 0, pc); print('  %06x %-10s %s' % (pc, b.hex(), fmt(r[1], r[2], r[3])))
print('  %06x %s  tabla 12..15 -> 10 0F 0D 0E' % (labels['TBL'], TBL4.hex()))
hook = asm('bg.jal', {'/': CAVE}, HOOK)
r = d.decode(hook, 0, HOOK); print('gancho %x: %s %s' % (HOOK, hook.hex(), fmt(r[1], r[2], r[3])))
print('rutina + tabla: %d bytes' % len(code))
json.dump({'CAVE': CAVE, 'code': code.hex(), 'HOOK': HOOK, 'hook': hook.hex()},
          open('cave_v2.json', 'w'), indent=1)

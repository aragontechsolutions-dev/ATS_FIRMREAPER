#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
patch_ir_lg.py v5 — LG 42LF652VX (placa TP.V56.PB801, MStar TSUMV56, CPU AEON R2):
obedecer al mando del SOYEA LKK32CMB con la misma función por botón que el
firmware Soyea. El resto del firmware no cambia (salvo --espejo-off).

Uso:
    pip install zopfli
    python3 patch_ir_lg.py MST_EXT_SPI_ISP_20220511_165639.BIN               # -> LG_42LF652VX_MANDO_SOYEA_v5.BIN
    python3 patch_ir_lg.py MST_EXT_SPI_ISP_20220511_165639.BIN --espejo-off  # -> ..._v5_ESPEJO_OFF.BIN
    python3 patch_ir_lg.py ARCHIVO.BIN --verify-only

Cadena del LG (detalle en README_MANDO_SOYEA.md):
  código IR --(cliente 0x39DEFE => mando 1)--> tabla flash 0x39DD94
  [u32 0x0100|código, u8 índice] --(0x303D2E)--> índice --(switch 0x2B4D48)--> KEY_xxx
La v1 tradujo el código Soyea al ÍNDICE, pero el LG lo vuelve a pasar por la
tabla: las teclas salían desplazadas. La v2 reescribe la tabla 0x39DD94 con los
códigos del Soyea y el índice que produce la misma KEY, y añade la lógica de la
cruceta del Soyea (VOL/CH que hacen de flechas dentro de los menús).
v3: también en el reproductor USB (OSD 0x10).
v5: como la v3, pero durante la reproducción a pantalla completa en el USB VOL+/VOL- vuelven a
    ser volumen (CH+/CH- siguen siendo ARRIBA/ABAJO). (La v4 devolvía también CH.)
"""
import hashlib
import struct
import sys
import zlib

FW_SIZE = 0x400000
HDR_OFF = 0x020000
APP_FLASH_START = 0x020080
APP_RAM_DEST = 0x240000
DEFLATE_OFF = 0x023880
RES_LIMIT = 0x1850D0          # primer recurso de la tabla TEL
APP_BASE = 0x243800
ORIG_SHA256 = "ef5c80550894357d7f7e6caf6367d8cf7f7a8931dd6c4757f2e01cd136bee871"

KEYTAB = 0x39DD94             # 45 entradas de 8 bytes (hasta 0x39DEFC)
KEYTAB_N = 45

# (código Soyea, índice LG, botón / KEY que produce en ambos firmwares)
SOYEA_MAP = [
    (0x00, 0x02, "0"), (0x01, 0x03, "1"), (0x02, 0x04, "2"), (0x03, 0x05, "3"),
    (0x04, 0x06, "4"), (0x05, 0x07, "5"), (0x06, 0x08, "6"), (0x07, 0x09, "7"),
    (0x08, 0x0A, "8"), (0x09, 0x0B, "9"),
    (0x0A, 0x0C, "-/-- (DASH)"),
    (0x0B, 0x1D, "RETURN (canal anterior)"),
    (0x0C, 0x01, "POWER"),
    (0x0D, 0x22, "SOURCE (fuente)"),
    (0x0E, 0x21, "MUTE"),
    (0x0F, 0x1C, "MENU"),
    (0x14, 0x11, "OK (SELECT)"),
    (0x15, 0x12, "VOL+ / DERECHA en menús"),
    (0x16, 0x13, "VOL- / IZQUIERDA en menús"),
    (0x17, 0x14, "CH+ / ARRIBA en menús"),
    (0x18, 0x15, "CH- / ABAJO en menús"),
    (0x19, 0x24, "PICTURE (modo imagen)"),
    (0x1A, 0x1F, "AUDIO"),
    (0x1C, 0x1E, "INFO"),
    (0x1D, 0x1A, "EXIT"),
    (0x20, 0x17, "FAV"),
    (0x21, 0x29, "ROJO"),
    (0x22, 0x2A, "VERDE"),
    (0x23, 0x2B, "AMARILLO"),
    (0x24, 0x2C, "AZUL"),
    (0x28, 0x4C, "PAGE UP"),
    (0x29, 0x4D, "PAGE DOWN"),
    (0x45, 0x26, "SLEEP"),
    (0x46, 0x43, "CC"),
    (0x47, 0x49, "SUBTITLE"),
    (0x49, 0x16, "CH.LIST (lista de canales)"),
    (0x4B, 0x51, "PLAY"),
    (0x4C, 0x53, "STOP"),
    (0x4E, 0x56, "PREV"),
    (0x53, 0x57, "NEXT"),
    (0x54, 0x54, "REW"),
    (0x55, 0x55, "FF"),
    (0x56, 0x19, "KEY 0x82 (función del fabricante)"),
    (0x57, 0x18, "KEY 0x81 (función del fabricante)"),
]
# Sin equivalente en el LG (el LG nunca genera esa KEY): Soyea 25 TV/RADIO,
# 40 MTS, 4F TEMP_DET, 51 EPG.


def _keytab_new():
    t = bytearray()
    for code, idx, _ in SOYEA_MAP:
        t += struct.pack("<IB3x", 0x0100 | code, idx)
    t += struct.pack("<IB3x", 0xFFFF, 0xFF)          # terminador, como el original
    while len(t) < KEYTAB_N * 8:
        t += struct.pack("<IB3x", 0xFFFF, 0xFF)
    assert len(t) == KEYTAB_N * 8
    return t.hex()


KEYTAB_ORIG_HEX = (
    "000100000100000001010000220000000201000003000000030100000400000004010000050000000501000006"
    "000000060100000700000007010000080000000801000009000000090100000a0000000a0100000b0000006101"
    "00000c0000000c010000020000000d0100001d000000480100001200000049010000130000004a010000140000"
    "004b0100001500000016010000210000000f0100001700000044010000240000006001000025000000620100001e"
    "00000041010000260000004201000023000000130100000d000000140100000e000000110100000f0000001201"
    "0000100000001001000011000000400100001c000000150100001a0000004c010000290000004d0100002a0000"
    "004e0100002b0000004f0100002c000000510100005600000052010000570000005a010000540000005c010000"
    "5500000056010000500000005401000053000000550100003f0000005701000027000000ffff0000ff000000")

# Rutina en 0x3BB400 (textos de depuración ASF-MJPEG sin referencias), generada con
# tools/build_cave_v2.py y verificada desensamblándola:
#   addi r1,r1,-8 ; sw 4(r1),r9 ; sw 0(r1),r10 ; mov r10,r3
#   jal 0x303D2E                          ; código -> índice (original)
#   lbz r7,0(r10) ; andi r7,r7,0x18 ; bnei r7,8,FIN      ; solo mando 1 (Soyea)
#   lhz r7,4(r10) ; addi r7,r7,-0x12 ; bgtui r7,3,FIN    ; solo índices 12..15 (VOL+/-, CH+/-)
#   jal 0x2E5A7E ; beqi r3,7,CONV                        ; OSD 7 (lista de fuentes del teclado)
#   sfeqi r3,0x10 ; bnf MENU                             ; OSD 0x10 = reproductor USB:
#     jal 0x278DC1 ; beqi r3,0,CONV                      ;   nada reproduciéndose -> flechas
#     jal 0x2C5152(0x129) ; bnei r3,0,CONV               ;   explorador de archivos visible -> flechas
#     reproducción a pantalla completa (v5): índice 12/13 (VOL+/-) -> FIN (volumen);
#                                            índice 14/15 (CH+/-) -> CONV (ARRIBA/ABAJO)
#   MENU: jal 0x303BA8 ; beqi r3,0,FIN                         ; OSD navegable (mismo criterio que el teclado del TV)
#   CONV: índice = {10,0F,0D,0E}[índice-0x12]            ; DERECHA, IZQUIERDA, ARRIBA, ABAJO
#   FIN: lwz r10,0(r1) ; lwz r9,4(r1) ; addi r1,r1,8 ; jr r9
CAVE = 0x3BB400
CAVE_HEX = ("1c21f80d21040d41008943e7e9124610ea0054e718d0e802ce08ea051ce7eed0e3027d"
            "e7e54cb6d06701a25c6201200097e7d7b320d0600132c8600129e7e13a2ad06000d608"
            "ea051ce7eed0e10119e4000019e7e90eaad06000ba08ea05c0c00781fcc6b4678cc710"
            "e6000cea050d41020d21061c21088529100f0d0e")
CAVE_ORIG_HEX = ("005b4153465f4d4a5045475d4669727374207061636b657420696e206672616d65284c"
                 "2921212121212121212121212121212121005b4153465f4d4a5045475d6d6964646c65"
                 "207061636b657420696e206672616d65284c2921212121212121212121212121212121"
                 "005b4153465f4d4a5045475d6d6964646c652070")

PATCHES = [
    (0x39DEFE, "bf00", "7837", "cliente del mando 1: 00 BF -> 37 78 (Soyea)"),
    (KEYTAB, KEYTAB_ORIG_HEX, _keytab_new(), "tabla código->índice del mando 1 con los códigos Soyea"),
    (0x2B4A60, "e409e59c", "e420d340", "gancho: jal 0x303D2E -> jal 0x3BB400"),
    (CAVE, CAVE_ORIG_HEX, CAVE_HEX, "rutina de la cruceta (125 bytes)"),
]
PATCH_ESPEJO_OFF = (0x25E644, "f8a10163", "f8010163",
                    "XC_INITDATA.bMirror: sb 0x163(r1),r5 (=1) -> sb 0x163(r1),r0 (=0)")

_T = []
for _i in range(256):
    _c = _i << 24
    for _ in range(8):
        _c = ((_c << 1) ^ 0x04C11DB7) & 0xFFFFFFFF if _c & 0x80000000 else (_c << 1) & 0xFFFFFFFF
    _T.append(_c)


def crc32_nr(data, crc=0):
    t = _T
    for b in data:
        crc = ((crc << 8) & 0xFFFFFFFF) ^ t[((crc >> 24) ^ b) & 0xFF]
    return crc


def read_header(fw):
    return list(struct.unpack_from("<6I", fw, HDR_OFF))


def extract_app(fw):
    d = zlib.decompressobj(-15)
    app = d.decompress(fw[DEFLATE_OFF:RES_LIMIT])
    if not d.eof:
        raise ValueError("el deflate de la app no termina antes del límite de recursos")
    end = RES_LIMIT - len(d.unused_data)
    tail = fw[end:end + 6]
    if struct.unpack_from("<I", tail, 0)[0] != len(app):
        raise ValueError("el tamaño de la cola no coincide con la app")
    return app, end, tail


def check_container(fw, label):
    h = read_header(fw)
    app, end, _ = extract_app(fw)
    assert h[0] == APP_FLASH_START and h[1] == APP_RAM_DEST, label + ": cabecera inesperada"
    assert h[3] == end + 6, label + ": fin de flash incoherente"
    assert h[2] == APP_RAM_DEST + (h[3] - APP_FLASH_START), label + ": fin RAM incoherente"
    assert h[3] <= RES_LIMIT, label + ": la app pisa los recursos"
    crc = crc32_nr(fw[APP_FLASH_START:h[3]])
    assert crc == h[5], "%s: CRC 0x%08X != 0x%08X" % (label, crc, h[5])
    return app


def state(app, patches):
    s = set()
    for addr, old, new, _ in patches:
        o = addr - APP_BASE
        cur = app[o:o + len(old) // 2].hex()
        s.add("orig" if cur == old else "nuevo" if cur == new else "otro")
    return s.pop() if len(s) == 1 else "mixto"


def deflate_best(data):
    try:
        import zopfli.zopfli as zz
        comp = zz.compress(data, numiterations=5, blocksplitting=1, gzip_mode=0)[2:-4]
    except ImportError:
        print("AVISO: zopfli no instalado (pip install zopfli); se usa zlib -9")
        c = zlib.compressobj(9, zlib.DEFLATED, -15, 8, zlib.Z_DEFAULT_STRATEGY)
        comp = c.compress(data) + c.flush()
    if zlib.decompressobj(-15).decompress(comp) != data:
        raise ValueError("la recompresión no es reversible")
    return comp


def build(fw, patches):
    app, _, tail = extract_app(fw)
    if state(app, patches) != "orig":
        raise ValueError("los bytes originales no coinciden (¿otro firmware o ya parcheado?)")
    app = bytearray(app)
    for addr, old, new, _ in patches:
        o = addr - APP_BASE
        assert len(old) == len(new)
        app[o:o + len(new) // 2] = bytes.fromhex(new)
    comp = deflate_best(bytes(app))
    new_tail = struct.pack("<I", len(app)) + tail[4:6]
    new_end = DEFLATE_OFF + len(comp) + len(new_tail)
    if new_end > RES_LIMIT:
        raise ValueError("la app recomprimida no cabe (0x%X > 0x%X)" % (new_end, RES_LIMIT))
    out = bytearray(fw)
    out[DEFLATE_OFF:RES_LIMIT] = b"\x00" * (RES_LIMIT - DEFLATE_OFF)
    out[DEFLATE_OFF:new_end] = comp + new_tail
    h = read_header(out)
    h[3] = new_end
    h[2] = APP_RAM_DEST + (new_end - APP_FLASH_START)
    h[5] = crc32_nr(bytes(out[APP_FLASH_START:new_end]))
    struct.pack_into("<6I", out, HDR_OFF, *h)
    return bytes(out)


def simulate(app):
    """Recorre la tabla del firmware resultante como lo hace 0x303D2E."""
    o = KEYTAB - APP_BASE
    tab = [struct.unpack_from("<IB", app, o + 8 * i) for i in range(KEYTAB_N)]
    for code, idx, name in SOYEA_MAP:
        hit = [v for c, v in tab if c == (0x0100 | code)]
        assert hit and hit[0] == idx, "tecla Soyea %02X: índice %r != %02X" % (code, hit, idx)
    assert struct.unpack_from("<H", app, 0x39DEFE - APP_BASE)[0] == 0x3778
    return len(SOYEA_MAP)


def verify(orig, new, patches):
    assert len(new) == FW_SIZE
    a0 = check_container(orig, "original")
    a1 = check_container(new, "modificado")
    assert len(a0) == len(a1) and state(a1, patches) == "nuevo"
    allowed = set()
    for addr, old, _, _ in patches:
        o = addr - APP_BASE
        allowed.update(range(o, o + len(old) // 2))
    diff = [i for i in range(len(a0)) if a0[i] != a1[i]]
    assert set(diff) <= allowed, "hay cambios fuera de los puntos de parche"
    assert orig[:HDR_OFF] == new[:HDR_OFF]
    assert orig[HDR_OFF + 0x18:DEFLATE_OFF] == new[HDR_OFF + 0x18:DEFLATE_OFF]
    assert orig[RES_LIMIT:] == new[RES_LIMIT:]
    n = simulate(a1)
    return len(diff), n


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__)
        sys.exit(1)
    patches = list(PATCHES)
    out = "LG_42LF652VX_MANDO_SOYEA_v5.BIN"
    if "--espejo-off" in sys.argv:
        patches.append(PATCH_ESPEJO_OFF)
        out = "LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN"
    fw = open(args[0], "rb").read()
    if len(fw) != FW_SIZE:
        sys.exit("ERROR: el archivo no mide 4 MB")
    sha = hashlib.sha256(fw).hexdigest()
    print("SHA-256:", sha, "(original analizado)" if sha == ORIG_SHA256 else "")
    app = check_container(fw, "entrada")
    print("Contenedor OK. Estado:", state(app, patches))
    if "--verify-only" in sys.argv:
        return
    new = build(fw, patches)
    nd, nk = verify(fw, new, patches)
    open(out, "wb").write(new)
    h = read_header(new)
    print("%s -> %d bytes cambiados en la app, %d teclas Soyea verificadas, fin flash 0x%X "
          "(límite 0x%X), CRC 0x%08X" % (out, nd, nk, h[3], RES_LIMIT, h[5]))
    print("SHA-256:", hashlib.sha256(new).hexdigest())


if __name__ == "__main__":
    main()

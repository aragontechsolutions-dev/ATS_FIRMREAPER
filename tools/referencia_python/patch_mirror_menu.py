#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
patch_mirror_menu.py — Firmware Konka "TV4-32K16" (placa TP.VST59S.PB813,
MStar TSUMV59, CPU AEON R2):
  1) espejo (mirror H+V) activable desde el MENÚ DE FÁBRICA ("MIRROR" ON/OFF),
  2) logo de arranque girado 180° (o un logo JPEG propio).

Uso:
    pip install zopfli pillow
    python3 patch_mirror_menu.py TV4-32K16.bin                  # genera TV4-32K16_MIRROR_MENU.bin
    python3 patch_mirror_menu.py TV4-32K16.bin --logo mi.jpg    # logo propio (JPEG baseline 1366x768)
    python3 patch_mirror_menu.py TV4-32K16.bin --sin-logo       # no tocar el logo
    python3 patch_mirror_menu.py ARCHIVO.bin --verify-only

Funcionamiento (detalle en README_MIRROR_MENU.md):
  - La opción "Logo" del menú de fábrica no la usaba ningún código de este
    firmware; se renombra a "MIRROR" y su variable (0x3EA57F, guardada en la
    base de datos) pasa a controlar el espejo. Valor por defecto de fábrica: ON.
  - Arranque: XC se inicia con bMirror = 1 (estado probado). Tras crear el
    contexto del GOP, la rutina 2 pone bH/bVMirror = 1.
  - Tras cargar la base de datos, la rutina 1 lee MIRROR; si está en OFF,
    desactiva el espejo del vídeo (XC) y del OSD (GOP).
  - El cambio en el menú se aplica al volver a encender el TV.

Direcciones = RAM (app descomprimida cargada en 0x203800).
"""
import hashlib
import io
import struct
import sys
import zlib

FW_SIZE = 0x400000
HDR_OFF = 0x020000
APP_FLASH_START = 0x020080
APP_RAM_DEST = 0x200000
DEFLATE_OFF = 0x023880
RES_LIMIT = 0x0F9028
APP_BASE = 0x203800
ORIG_SHA256 = "32c529658cdefd4beee9fd408a8d52586bbfa7cf4416084a7784f76dcadaad9e"

# Logo: recurso JPEG de la tabla TEL
LOGO_OFF = 0x29B1B8
LOGO_SLOT = 0x20138          # tamaño del recurso
LOGO_JPEG_LEN = 131381       # JPEG original; tras él quedan 3 bytes (00 47 0E) que se conservan
LOGO_SHA256 = None           # se calcula del original al vuelo

# Rutinas nuevas, en una zona de textos de depuración JPEG sin uso (0x33AB66–0x33B9A1)
STR_ADDR = 0x33B20C
CAVE1 = 0x33B214
CAVE2 = 0x33B25C
CAVE_END = 0x33B289
STR_MIRROR = b"MIRROR\x00\x00"           # 8 bytes: 0x33B20C..0x33B213

# Rutina 1 (tras cargar stGenSetting; termina saltando a 0x25DA52):
#   movhi r7,0x3f ; lbz r7,-0x5a81(r7)        ; r7 = MIRROR (0x3EA57F)
#   bnei  r7,0,END                            ; ON -> nada que hacer
#   movhi r6,0x3f ; sb 0x779b(r6),r0          ; XC_INITDATA(copia).bMirror = 0
#   movhi r6,0x3e ; lwz r6,-0x7b4c(r6)        ; ctx XC (0x3D84B4)
#   beqi r6,0,GOP ; lwz r6,0xc(r6) ; beqi r6,0,GOP
#   sw 0xb8(r6),r0                            ; modo espejo XC = 0
# GOP: movhi r6,0x3d ; lwz r6,0x5aa8(r6) ; beqi r6,0,END
#   lwz r6,0(r6) ; beqi r6,0,END
#   sb 0xe(r6),r0 ; sb 0xf(r6),r0             ; bH/bVMirror del OSD = 0
# END: j 0x25DA52
CAVE1_HEX = ("c0e007e1f0e7a57fd0e001e6c0c007e1f806779bc0c007c1ecc684b6d0c0007a"
             "0cc60ed0c00042ec0600b8c0c007a1ecc65aaad0c0008a0cc602d0c000521806"
             "0e18060fe7e44ff5")
# Rutina 2 (envoltorio de la creación del contexto GOP 0x2AE8CB):
#   addi r1,r1,-4 ; sw 0(r1),r9 ; jal 0x2ae8cb
#   movhi r6,0x3d ; lwz r6,0x5aa8(r6) ; beqi r6,0,E2 ; lwz r6,0(r6) ; beqi r6,0,E2
#   movi r5,1 ; sb 0xe(r6),r5 ; sb 0xf(r6),r5
# E2: lwz r9,0(r1) ; addi r1,r1,4 ; jr r9          (r3 = resultado, intacto)
CAVE2_HEX = ("1c21fc0d2100e7ee6cd2c0c007a1ecc65aaad0c0009a0cc602d0c0006298a118"
             "a60e18a60f0d21021c21048529")

ORIG_ZONE_HEX = ("7533324d524342756641646472203d203078256c582c207533324d5243427566"
                 "53697a65203d203078256c582c207533324d52434275664f6666736574203d20"
                 "3078256c580a007533324d574342756641646472203d203078256c582c207531"
                 "364d57434275664c696e654e756d203d2025640a005363616c65446f77")

# (dirección, original, nuevo, descripción)
PATCHES = [
    (0x21DA41, "f801042b", "f9c1042b", "XC_INITDATA.bMirror = 1 al iniciar (r14 = 1)"),
    (0x25E965, "e7ffe1da", "e41b915e", "gancho 1: jal 0x25DA52 -> jal rutina 1"),
    (0x2B29A7, "e7ff7e48", "e411116a", "gancho 2: jal 0x2AE8CB -> jal rutina 2"),
    (0x32A0B4, "16713300", "0cb23300", "etiqueta del menú: 'Logo' -> 'MIRROR'"),
    (STR_ADDR, ORIG_ZONE_HEX, (STR_MIRROR + bytes.fromhex(CAVE1_HEX) + bytes.fromhex(CAVE2_HEX)).hex(),
     "texto MIRROR + rutinas 1 y 2"),
]

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


def state(app):
    s = set()
    for addr, old, new, _ in PATCHES:
        o = addr - APP_BASE
        cur = app[o:o + len(old) // 2].hex()
        s.add("orig" if cur == old else "menu" if cur == new else "otro")
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


def jpeg_info(j):
    """Devuelve (ancho, alto, progresivo) o lanza error si no es JPEG válido."""
    if j[:3] != b"\xff\xd8\xff":
        raise ValueError("no es un JPEG")
    i = 2
    while i < len(j) - 4:
        if j[i] != 0xFF:
            raise ValueError("JPEG mal formado")
        m = j[i + 1]
        if m in (0xD8, 0x01) or 0xD0 <= m <= 0xD7:
            i += 2
            continue
        L = struct.unpack(">H", j[i + 2:i + 4])[0]
        if m in (0xC0, 0xC1, 0xC2):
            h, w = struct.unpack(">HH", j[i + 5:i + 9])
            return w, h, m == 0xC2
        i += 2 + L
    raise ValueError("JPEG sin cabecera SOF")


def make_logo(fw, custom=None):
    slot = fw[LOGO_OFF:LOGO_OFF + LOGO_SLOT]
    orig = slot[:LOGO_JPEG_LEN]
    assert orig[-2:] == b"\xff\xd9", "el recurso del logo no es el esperado"
    if custom:
        new = open(custom, "rb").read()
    else:
        from PIL import Image
        im = Image.open(io.BytesIO(orig))
        b = io.BytesIO()
        im.convert("RGB").rotate(180).save(b, "JPEG", qtables=im.quantization, subsampling=0,
                                           optimize=False, progressive=False, restart_marker_blocks=171)
        new = b.getvalue()
    w, h, prog = jpeg_info(new)
    if (w, h) != (1366, 768) or prog:
        raise ValueError("el logo debe ser JPEG baseline 1366x768 (es %dx%d%s)" % (w, h, " progresivo" if prog else ""))
    if len(new) > LOGO_JPEG_LEN:
        raise ValueError("el logo ocupa %d bytes; máximo %d" % (len(new), LOGO_JPEG_LEN))
    return new + b"\x00" * (LOGO_JPEG_LEN - len(new)) + slot[LOGO_JPEG_LEN:]


def build(fw, logo=True, custom=None):
    app, _, tail = extract_app(fw)
    if state(app) != "orig":
        raise ValueError("los bytes originales no coinciden (¿otro firmware o ya parcheado?)")
    app = bytearray(app)
    for addr, old, new, _ in PATCHES:
        o = addr - APP_BASE
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
    if logo:
        out[LOGO_OFF:LOGO_OFF + LOGO_SLOT] = make_logo(fw, custom)
    return bytes(out)


def verify(orig, new, logo):
    assert len(new) == FW_SIZE
    a0 = check_container(orig, "original")
    a1 = check_container(new, "modificado")
    assert len(a0) == len(a1) and state(a1) == "menu"
    allowed = set()
    for addr, old, _, _ in PATCHES:
        o = addr - APP_BASE
        allowed.update(range(o, o + len(old) // 2))
    diff = [i for i in range(len(a0)) if a0[i] != a1[i]]
    assert set(diff) <= allowed, "hay cambios fuera de los puntos de parche"
    assert orig[:HDR_OFF] == new[:HDR_OFF]
    assert orig[HDR_OFF + 0x18:DEFLATE_OFF] == new[HDR_OFF + 0x18:DEFLATE_OFF]
    if logo:
        assert orig[RES_LIMIT:LOGO_OFF] == new[RES_LIMIT:LOGO_OFF]
        assert orig[LOGO_OFF + LOGO_SLOT:] == new[LOGO_OFF + LOGO_SLOT:]
        jpeg_info(new[LOGO_OFF:LOGO_OFF + LOGO_SLOT])
    else:
        assert orig[RES_LIMIT:] == new[RES_LIMIT:]
    return len(diff)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__)
        sys.exit(1)
    custom = None
    if "--logo" in sys.argv:
        custom = sys.argv[sys.argv.index("--logo") + 1]
        args = [a for a in args if a != custom]
    logo = "--sin-logo" not in sys.argv
    fw = open(args[0], "rb").read()
    if len(fw) != FW_SIZE:
        sys.exit("ERROR: el archivo no mide 4 MB")
    sha = hashlib.sha256(fw).hexdigest()
    print("SHA-256:", sha, "(original analizado)" if sha == ORIG_SHA256 else "")
    app = check_container(fw, "entrada")
    print("Contenedor OK. Estado:", state(app))
    if "--verify-only" in sys.argv:
        return
    new = build(fw, logo, custom)
    n = verify(fw, new, logo)
    out = "TV4-32K16_MIRROR_MENU.bin"
    open(out, "wb").write(new)
    h = read_header(new)
    print("%s -> %d bytes cambiados en la app, fin flash 0x%X, CRC 0x%08X" % (out, n, h[3], h[5]))
    print("Logo:", "propio (%s)" % custom if custom else ("girado 180°" if logo else "sin cambios"))
    print("SHA-256:", hashlib.sha256(new).hexdigest())
    for addr, old, nw, desc in PATCHES:
        print("  0x%06X  %d bytes  %s" % (addr, len(old) // 2, desc))


if __name__ == "__main__":
    main()

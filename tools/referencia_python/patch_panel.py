#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
patch_panel.py — Adapta el firmware del TV ATEC 32L18D (placa MStar, ID
BD_MST061B_10AZU_14014, SPI de 4 MB) al panel LG.Philips LP141WX1 (TL)(A2):
1280x800, LVDS de un canal, 6 bits.

Uso:
    python3 patch_panel.py Atec_32_KPC_Salva.BIN            # genera las 2 variantes
    python3 patch_panel.py Atec_32_KPC_Salva.BIN --verify-only

Salida:
    Atec32L18D_LP141WX1_6bit.BIN  (variante principal: salida LVDS a 6 bits)
    Atec32L18D_LP141WX1_8bit.BIN  (respaldo: misma resolución, salida a 8 bits;
                                   el panel solo lee los pares 0-2, exige mapeo JEIDA)

Estructura del firmware (determinada por análisis del volcado; ver README.md):
  0x020000  Cabecera de la aplicación (6 x u32 little-endian):
              +0x00 inicio en flash      = 0x00020080
              +0x04 destino en RAM       = 0x00240000
              +0x08 fin en RAM           = destino + (fin_flash - inicio_flash)
              +0x0C fin en flash         = 0x001E9283 (original)
              +0x10 0
              +0x14 CRC-32 no reflejado (poly 0x04C11DB7, init 0, sin xorout)
                    de flash[inicio : fin]
  0x023080  Stub de arranque (código, sin comprimir)
  0x023880  Aplicación comprimida con deflate crudo (RFC 1951)
            seguida de: u32 LE tamaño descomprimido + 2 bytes (F8 71, se conservan)
  0x1E9288  Primer recurso de la tabla TEL (límite que no se puede pisar)

Tabla de paneles (en la aplicación descomprimida), estructuras de 92 bytes.
La entrada usada por el TV es "WXGA_AU20_T200XW02" (1366x768, un canal),
en el offset 0x2A0278 de la aplicación descomprimida.
"""
import hashlib
import struct
import sys
import zlib

# ---------------------------------------------------------------- constantes
FW_SIZE         = 0x400000
HDR_OFF         = 0x020000
APP_FLASH_START = 0x020080
APP_RAM_DEST    = 0x240000
DEFLATE_OFF     = 0x023880
RES_LIMIT       = 0x1E9288          # no se puede escribir en o después de aquí
PANEL_ENTRY     = 0x2A0278          # offset dentro de la app descomprimida
ENTRY_SIZE      = 0x5C
ORIG_SHA256     = "19491133011eaca67db793dcfb068771e19a58dac42eff849ca30993fbad647b"

# Valores originales esperados en la entrada (para comprobar que es la correcta)
EXPECT = {"width": 1366, "height": 768, "htotal": 1560, "vtotal": 806, "dclk": 76}

# ---------------------------------------------------------------- CRC-32 no reflejado
_T = []
for _i in range(256):
    _c = _i << 24
    for _ in range(8):
        _c = ((_c << 1) ^ 0x04C11DB7) & 0xFFFFFFFF if _c & 0x80000000 else (_c << 1) & 0xFFFFFFFF
    _T.append(_c)


def crc32_nr(data: bytes, crc: int = 0) -> int:
    t = _T
    for b in data:
        crc = ((crc << 8) & 0xFFFFFFFF) ^ t[((crc >> 24) ^ b) & 0xFF]
    return crc


# ---------------------------------------------------------------- utilidades
def read_header(fw: bytes):
    return list(struct.unpack_from("<6I", fw, HDR_OFF))


def extract_app(fw: bytes):
    """Descomprime la aplicación y devuelve (datos, fin_del_deflate, cola_6_bytes)."""
    d = zlib.decompressobj(-15)
    app = d.decompress(fw[DEFLATE_OFF:RES_LIMIT])
    if not d.eof:
        raise ValueError("el bloque deflate no termina antes del límite de recursos")
    end = RES_LIMIT - len(d.unused_data)
    tail = fw[end:end + 6]
    size = struct.unpack_from("<I", tail, 0)[0]
    if size != len(app):
        raise ValueError("el tamaño de la cola (%d) no coincide con lo descomprimido (%d)" % (size, len(app)))
    return app, end, tail


def check_container(fw: bytes, label: str) -> bytes:
    """Valida cabecera + CRC + cola y devuelve la app descomprimida."""
    h = read_header(fw)
    app, end, tail = extract_app(fw)
    assert h[0] == APP_FLASH_START, "%s: inicio de flash inesperado" % label
    assert h[1] == APP_RAM_DEST, "%s: destino RAM inesperado" % label
    assert h[3] == end + 6, "%s: fin de flash (0x%X) no coincide con la cola (0x%X)" % (label, h[3], end + 6)
    assert h[2] == APP_RAM_DEST + (h[3] - APP_FLASH_START), "%s: fin RAM incoherente" % label
    assert h[3] <= RES_LIMIT, "%s: la app pisa los recursos" % label
    crc = crc32_nr(fw[APP_FLASH_START:h[3]])
    assert crc == h[5], "%s: CRC 0x%08X != cabecera 0x%08X" % (label, crc, h[5])
    return app


def entry_fields(app: bytes, off: int = PANEL_ENTRY) -> dict:
    e = app[off:off + ENTRY_SIZE]
    u16 = lambda k: struct.unpack_from("<H", e, k)[0]
    u32 = lambda k: struct.unpack_from("<I", e, k)[0]
    return {
        "flags0": e[4], "flags1": e[5],
        "on1": u16(12), "on2": u16(14), "off1": u16(16), "off2": u16(18),
        "hsync": e[20], "hbp": e[21], "vsync": e[22], "vbp": e[23],
        "hstart": u16(24), "vstart": u16(26), "width": u16(28), "height": u16(30),
        "htotal_max": u16(32), "htotal": u16(34), "htotal_min": u16(36),
        "vtotal_max": u16(38), "vtotal": u16(40), "vtotal_min": u16(42),
        "dclk_max": e[44], "dclk": e[45], "dclk_min": e[46],
        "ti_bitmode": u32(64), "out_bitmode": u32(68),
        "max_set": u32(76), "min_set": u32(80), "out_timing_mode": u32(84),
    }


# ---------------------------------------------------------------- parche
# Valores del LP141WX1 (hoja de datos LG.Philips, Ver 0.2, tabla 6)
LP141 = dict(
    hsync=32, hbp=80, vsync=6, vbp=15,
    hstart=32 + 80, vstart=6 + 15,           # 112 / 21
    width=1280, height=800,
    htotal_max=1480, htotal=1440, htotal_min=1368,
    vtotal_max=848, vtotal=823, vtotal_min=809,
    dclk_max=75, dclk=71, dclk_min=66,        # MHz (hoja: 75.4 / 71.0 / 66.4)
)
# Constante del PLL deducida de la entrada original:
#   MaxSET * MinDCLK = 2157070 * 60 = 129424200 ; MinSET * MaxDCLK = 1470729 * 88 = 129424152
SET_K = 129424176


def patch_entry(app: bytearray, six_bit: bool):
    o = PANEL_ENTRY
    f = entry_fields(app)
    for k, v in EXPECT.items():
        if f[k] != v:
            raise ValueError("la entrada del panel no es la esperada (%s=%s)" % (k, f[k]))
    if f["flags0"] & 0x20:
        raise ValueError("la entrada original es de doble canal; no se esperaba")

    p = LP141
    app[o + 20] = p["hsync"]
    app[o + 21] = p["hbp"]
    app[o + 22] = p["vsync"]
    app[o + 23] = p["vbp"]
    struct.pack_into("<HHHH", app, o + 24, p["hstart"], p["vstart"], p["width"], p["height"])
    struct.pack_into("<HHH", app, o + 32, p["htotal_max"], p["htotal"], p["htotal_min"])
    struct.pack_into("<HHH", app, o + 38, p["vtotal_max"], p["vtotal"], p["vtotal_min"])
    app[o + 44] = p["dclk_max"]
    app[o + 45] = p["dclk"]
    app[o + 46] = p["dclk_min"]
    # Límites del PLL ajustados al rango de reloj del panel (protege el panel)
    struct.pack_into("<II", app, o + 76, round(SET_K / p["dclk_min"]), round(SET_K / p["dclk_max"]))

    if six_bit:
        # m_ucTiBitMode: 2 = 8 bits (valor original), 3 = 6 bits
        # m_ucOutputFormatBitMode: 0 = original, 1 = 6 bits
        struct.pack_into("<I", app, o + 64, 3)
        struct.pack_into("<I", app, o + 68, 1)
        app[o + 4] |= 0x01   # m_bPanelDither = 1 (suaviza degradados en 6 bits)


def build(fw: bytes, six_bit: bool) -> bytes:
    app, old_end, tail = extract_app(fw)
    app = bytearray(app)
    patch_entry(app, six_bit)

    c = zlib.compressobj(9, zlib.DEFLATED, -15, 8, zlib.Z_DEFAULT_STRATEGY)
    comp = c.compress(bytes(app)) + c.flush()
    new_tail = struct.pack("<I", len(app)) + tail[4:6]      # mismo tamaño + mismos 2 bytes
    new_end = DEFLATE_OFF + len(comp) + len(new_tail)
    if new_end > RES_LIMIT:
        raise ValueError("la app recomprimida no cabe (fin 0x%X > 0x%X)" % (new_end, RES_LIMIT))

    out = bytearray(fw)
    # borrar la zona antigua (hasta el límite de recursos) con ceros, como en el original
    out[DEFLATE_OFF:RES_LIMIT] = b"\x00" * (RES_LIMIT - DEFLATE_OFF)
    out[DEFLATE_OFF:new_end] = comp + new_tail

    h = read_header(out)
    h[3] = new_end
    h[2] = APP_RAM_DEST + (new_end - APP_FLASH_START)
    h[5] = crc32_nr(bytes(out[APP_FLASH_START:new_end]))
    struct.pack_into("<6I", out, HDR_OFF, *h)
    return bytes(out)


def verify_output(orig: bytes, new: bytes, six_bit: bool, label: str):
    assert len(new) == FW_SIZE, "tamaño incorrecto"
    app_new = check_container(new, label)
    app_old = check_container(orig, "original")
    assert len(app_new) == len(app_old)
    diff = [i for i in range(len(app_old)) if app_old[i] != app_new[i]]
    assert all(PANEL_ENTRY <= i < PANEL_ENTRY + ENTRY_SIZE for i in diff), \
        "%s: hay cambios fuera de la entrada del panel" % label
    # Fuera de la zona [0x20000, 0x1E9288) el archivo tiene que ser idéntico
    assert orig[:HDR_OFF] == new[:HDR_OFF]
    assert orig[HDR_OFF + 0x18:DEFLATE_OFF] == new[HDR_OFF + 0x18:DEFLATE_OFF]
    assert orig[RES_LIMIT:] == new[RES_LIMIT:]
    f = entry_fields(app_new)
    assert (f["width"], f["height"], f["htotal"], f["vtotal"], f["dclk"]) == (1280, 800, 1440, 823, 71)
    assert f["ti_bitmode"] == (3 if six_bit else 2)
    return f, len(diff)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    src = sys.argv[1]
    fw = open(src, "rb").read()
    if len(fw) != FW_SIZE:
        sys.exit("ERROR: el archivo no mide 4 MB (%d bytes)" % len(fw))
    sha = hashlib.sha256(fw).hexdigest()
    print("SHA-256 origen:", sha, "(coincide con el analizado)" if sha == ORIG_SHA256 else "(DISTINTO al analizado: revisar)")
    app = check_container(fw, "original")
    print("Original OK. Panel actual:", entry_fields(app))
    if "--verify-only" in sys.argv:
        return

    for six_bit, name in ((True, "Atec32L18D_LP141WX1_6bit.BIN"), (False, "Atec32L18D_LP141WX1_8bit.BIN")):
        new = build(fw, six_bit)
        f, nd = verify_output(fw, new, six_bit, name)
        open(name, "wb").write(new)
        h = read_header(new)
        print("\n%s  -> %d bytes cambiados en la app, fin flash 0x%X, CRC 0x%08X" % (name, nd, h[3], h[5]))
        print("  SHA-256:", hashlib.sha256(new).hexdigest())
        print("  Panel:", f)


if __name__ == "__main__":
    main()

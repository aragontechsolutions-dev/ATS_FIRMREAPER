#!/usr/bin/env python3
"""Genera los perfiles JSON integrados a partir de las constantes de los scripts de referencia
(tools/referencia_python). Así los bytes de los parches no se copian a mano.

Uso:  python3 tools/perfiles/generar_perfiles.py
Salida: src/FirmReaper.Core/Profiles/Builtin/*.json
El perfil ATEC necesita además el original firmware_local/originales/Atec_32_KPC_Salva.BIN (los bytes de
la entrada del panel se calculan con patch_entry() de patch_panel.py sobre la app descomprimida).
"""
import json
import os
import types

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(RAIZ, "tools", "referencia_python")
SALIDA = os.path.join(RAIZ, "src", "FirmReaper.Core", "Profiles", "Builtin")


def cargar(nombre):
    """Carga un script como módulo SIN ejecutar main() (solo define constantes y funciones)."""
    ruta = os.path.join(REF, nombre)
    mod = types.ModuleType(nombre[:-3])
    mod.__file__ = ruta
    exec(compile(open(ruta, encoding="utf-8").read(), ruta, "exec"), mod.__dict__)
    return mod


def parche(addr, old, new, desc):
    assert len(old) == len(new) and len(old) % 2 == 0 and old != new
    return {"direccion": "0x%06X" % addr, "original": old.lower(), "nuevo": new.lower(), "descripcion": desc}


def zona(desde, hasta, motivo):
    return {"desde": "0x%06X" % desde, "hasta": "0x%06X" % hasta, "motivo": motivo}


def guardar(nombre, perfil):
    ruta = os.path.join(SALIDA, nombre)
    with open(ruta, "w", encoding="utf-8", newline="\n") as f:
        json.dump(perfil, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("escrito", ruta)


# ------------------------------------------------------------------ LG 42LF652VX
lg = cargar("patch_ir_lg.py")
p_cliente, p_tabla, p_gancho, p_rutina = [parche(*x) for x in lg.PATCHES]
p_espejo = parche(*lg.PATCH_ESPEJO_OFF)
# Funciones (índice del switch 0x2B4D48 del LG) con nombre documentado en README_LG_MANDO_SOYEA.md:
# las KEY que producen las teclas Soyea, las flechas de la rutina de la cruceta ({10,0F,0D,0E}) y, sin nombre
# identificado, los índices que usa el mando LG original.
FUNCIONES_LG = [
    (0x01, "POWER (encender/apagar)"), (0x02, "KEY_0"), (0x03, "KEY_1"), (0x04, "KEY_2"), (0x05, "KEY_3"),
    (0x06, "KEY_4"), (0x07, "KEY_5"), (0x08, "KEY_6"), (0x09, "KEY_7"), (0x0A, "KEY_8"), (0x0B, "KEY_9"),
    (0x0C, "DASH (-/--)"), (0x0D, "ARRIBA"), (0x0E, "ABAJO"), (0x0F, "IZQUIERDA"), (0x10, "DERECHA"),
    (0x11, "SELECT (OK)"), (0x12, "VOLUME_PLUS (en menús: DERECHA)"), (0x13, "VOLUME_MINUS (en menús: IZQUIERDA)"),
    (0x14, "CHANNEL_PLUS (en menús: ARRIBA)"), (0x15, "CHANNEL_MINUS (en menús: ABAJO)"),
    (0x16, "CHANNEL_LIST (lista de canales)"), (0x17, "CHANNEL_FAV_LIST (favoritos)"),
    (0x18, "KEY 0x81 (función del fabricante)"), (0x19, "KEY 0x82 (función del fabricante)"),
    (0x1A, "EXIT"), (0x1C, "MENU"), (0x1D, "CHANNEL_RETURN (canal anterior)"), (0x1E, "INFO"), (0x1F, "AUDIO"),
    (0x21, "MUTE"), (0x22, "INPUT_SOURCE (fuente)"), (0x24, "PICTURE (modo de imagen)"), (0x26, "SLEEP"),
    (0x29, "ROJO"), (0x2A, "VERDE"), (0x2B, "AMARILLO"), (0x2C, "AZUL"), (0x43, "CC"), (0x49, "SUBTITLE"),
    (0x4C, "PAGE_UP"), (0x4D, "PAGE_DOWN"), (0x51, "PLAY"), (0x53, "STOP"), (0x54, "REWIND"), (0x55, "FF"),
    (0x56, "PREVIOUS"), (0x57, "NEXT"),
    (0x23, "función LG 0x23 (sin identificar)"), (0x25, "función LG 0x25 (sin identificar)"),
    (0x27, "función LG 0x27 (sin identificar)"), (0x3F, "función LG 0x3F (sin identificar)"),
    (0x50, "función LG 0x50 (sin identificar)"),
]
# Botones del mando Soyea LKK32CMB (código NEC del botón, cliente 37 78) y su rótulo.
BOTONES_SOYEA = [(c, n) for c, _, n in lg.SOYEA_MAP] + [
    (0x25, "TV/RADIO"), (0x40, "MTS"), (0x4F, "TEMP_DET"), (0x51, "EPG")]

guardar("lg-42lf652vx-soyea.json", {
    "formato": 1,
    "id": "lg-42lf652vx-soyea",
    "version": 8,
    "nombre": "LG 42LF652VX (TP.VST56.PB801) — mando Soyea LKK32CMB y logo",
    "descripcion": "Obedecer al mando Soyea LKK32CMB con la misma función por botón que el firmware Soyea; "
                   "cruceta como flechas en menús y USB (v5). El espejo de imagen y menús se cambia mejor en el menú "
                   "de fábrica (MIRROR); aquí solo su valor inicial. "
                   "Logo de arranque (zona del logo capturado 0x370000): girado o propio.",
    "estado": "verificado",
    "original": {"sha256": lg.ORIG_SHA256},
    "app": {"baseRam": "0x%06X" % lg.APP_BASE},
    "compresion": {"metodo": "zopfli", "iteraciones": 5},
    "zonasPermitidas": [
        zona(0x25E644, 0x25E648, "instrucción que fija XC_INITDATA.bMirror"),
        zona(0x2B4A60, 0x2B4A64, "llamada a 0x303D2E en la rutina de teclas (gancho)"),
        zona(lg.KEYTAB, 0x39DF00, "tabla código→índice del mando 1 y códigos de cliente"),
        zona(lg.CAVE, 0x3BB480, "textos de depuración ASF-MJPEG sin referencias (rutina nueva)"),
    ],
    "parchesFijos": [],
    "opciones": [
        {"id": "mando", "etiqueta": "Mando Soyea LKK32CMB", "tipo": "lista", "porDefecto": "si",
         "valores": [
             {"id": "si", "etiqueta": "Sí (v5)", "parches": [p_cliente, p_tabla, p_gancho, p_rutina]},
             {"id": "no", "etiqueta": "No (mando LG original)", "parches": []},
         ]},
        # Espejo: el parche solo cambia el valor INICIAL (XC_INITDATA.bMirror). Durante el arranque (secuencia por
        # pasos 0x2BED0F → 0x2AFC1C) el TV aplica el ajuste "MIRROR" del menú de fábrica (RAM 0x53ED78) a vídeo y
        # menús; ver docs/historial/README_ESPEJO_ATEC_LG.md. Las etiquetas lo dicen; los bytes no cambian.
        {"id": "espejo", "etiqueta": "Espejo al iniciar (después manda MIRROR del menú de fábrica)", "tipo": "lista",
         "porDefecto": "original",
         "valores": [
             {"id": "original", "etiqueta": "Como el original (activado)", "parches": []},
             {"id": "off", "etiqueta": "Desactivado al iniciar (mejor: MIRROR = OFF en el menú de fábrica)",
              "parches": [p_espejo]},
         ]},
        # Logo de arranque: el programa del TV (función 0x2C238C) lee 0x40000 bytes desde 0x370000 (logo
        # "capturado", el de "LG Life's Good") si el ajuste de logo vale 2; si no, usa el recurso TEL 0x0301
        # (imagen azul lisa). 0x370000 = inicio de la zona escribible por el TV (msAPI_Flash_Init).
        {"id": "logo", "etiqueta": "Logo de arranque (zona 0x370000)", "tipo": "lista", "porDefecto": "original",
         "valores": [
             {"id": "original", "etiqueta": "Sin cambios", "parches": []},
             {"id": "girado", "etiqueta": "Girado 180° — sin probar en el TV", "parches": [], "logo": "girar"},
             {"id": "propio", "etiqueta": "Propio (imagen; se ajusta a 1376x768) — sin probar en el TV",
              "parches": [], "logo": "propio"},
         ]},
    ],
    "logo": {"zonaFlash": {"desde": "0x370000", "hasta": "0x3B0000", "relleno": "0xFF"},
             "longitudJpeg": 27753, "ancho": 1376, "alto": 768},
    "salidasConocidas": [
        {"opciones": {"mando": "si", "espejo": "original", "logo": "original"},
         "sha256": "17918f8fa6607bb802fdf6c2e71f554b547a390201eb65b9e704494b9fd9b002",
         "archivo": "LG_42LF652VX_MANDO_SOYEA_v5.BIN"},
        {"opciones": {"mando": "si", "espejo": "off", "logo": "original"},
         "sha256": "3a836aee3dacb7d60fade3f7e834e8578309156e58a02e365e122fe236fed599",
         "archivo": "LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN"},
    ],
    # Código de cliente del mando 1: en memoria en orden inverso al de envío (el Soyea "37 78", probado en el TV,
    # está como 78 37). "Mando Soyea = sí" lo cambia a 37 78; el usuario puede poner otro
    # (ver docs/historial/README_MANDO_GENERICO.md).
    "clienteMando": {"direccion": "0x39DEFE", "formato": "bytes-invertidos", "original": "00 BF"},
    "tablaMando": {
        "formato": "lista8",
        "nota": "Los botones con función VOLUME_± o CHANNEL_± hacen además de flechas dentro de los menús y del "
                "USB (lo decide la rutina de la cruceta de la v5 según la función, no según el botón).",
        "direccion": "0x%06X" % lg.KEYTAB,
        "entradas": lg.KEYTAB_N,
        "prefijoCodigo": "0x0100",
        "opcion": "mando",
        "valor": "si",
        "funciones": [{"indice": "0x%02X" % i, "nombre": n} for i, n in FUNCIONES_LG],
        "botones": [{"codigo": "0x%02X" % c, "nombre": n} for c, n in BOTONES_SOYEA],
    },
})

# ------------------------------------------------------------------ Konka TV4-32K16
kk = cargar("patch_mirror_menu.py")
menu = [parche(*x) for x in kk.PATCHES]

# Mando del Konka (análisis en docs/historial/README_MANDO_GENERICO.md):
# - 0x38FFA2: código de cliente u16 (el decodificador 0x2A60DF pone el 1.er byte en la parte alta → en memoria
#   queda en orden inverso al de envío). Original 07 07.
# - 0x38FFA4: lista de 46 entradas de 4 bytes [código, tecla, 0, 0], terminada en tecla 0xFF (45 teclas);
#   al arrancar (0x25F519) se copia a la tabla RAM 0x3EA7D4 indexada por código. Detrás hay otros datos: no crece.
# - La tecla usa la MISMA numeración que los índices del LG (misma familia TP.): dígitos 0x02–0x0B, flechas
#   0x0D–0x10, OK 0x11, VOL/CH 0x12–0x15 (0x25F971 convierte flechas ↔ VOL/CH igual que el LG).
KONKA_TABLA_HEX = (
    "02010000011f00006e21000028220000212800002427000004030000050400000605000008060000090700000a080000"
    "0c0900000d0a00000e0b00002c0c00001102000013200000071200000b13000012140000101500001e1c00000f1e0000"
    "1a1b00002d1a000068110000650f000062100000600d0000610e000047430000461d000045290000482a0000502b0000"
    "3d2c00003762000041610000355900003e2300001f5d0000445e0000422d0000432e0000ffff0000"
)


# Versión "espejo fijo" (TV4-32K16_MIRROR.bin, SHA-256 98a919fb…, la primera que funcionó en el TV).
# No hay script: los parches se obtuvieron comparando ese binario con el original y se desensamblaron
# con tools/aeon/aeondis.py. 0x21DA41 es el mismo cambio que en la versión de menú.
GOP_LBZ_E = ("10e70e", "50e001", "GOP: lbz r7,0xe(r7) -> ori r7,r0,0x1 (bandera de espejo del OSD = 1)")
GOP_LBZ_F = ("10e70f", "50e001", "GOP: lbz r7,0xf(r7) -> ori r7,r0,0x1 (bandera de espejo del OSD = 1)")
GOP_LWZ_C = ("0ce70e", "04e001", "GOP: lwz r7,0xc(r7) -> movhi r7,0x1 (palabra de banderas de espejo fija)")
fijo_gop = [
    (0x2AE569, GOP_LBZ_E), (0x2AE576, GOP_LBZ_F), (0x2B0E49, GOP_LWZ_C), (0x2B0E61, GOP_LBZ_E),
    (0x2B0E76, GOP_LBZ_F), (0x2B0EE4, GOP_LBZ_F), (0x2B0F40, GOP_LWZ_C), (0x2B1301, GOP_LBZ_E),
    (0x2B132B, GOP_LBZ_F), (0x2B13C7, GOP_LWZ_C), (0x2B13DF, GOP_LBZ_E), (0x2B13F4, GOP_LBZ_F),
    (0x2B1414, GOP_LBZ_F), (0x2B1512, GOP_LBZ_E),
]
fijo = [parche(*kk.PATCHES[0])] + [parche(a, o, n, d) for a, (o, n, d) in fijo_gop]

NOMBRES_LG = dict(FUNCIONES_LG)
_kt = bytes.fromhex(KONKA_TABLA_HEX)
KONKA_TECLAS = []                     # (código, tecla) del original, en orden
for _i in range(0, len(_kt), 4):
    if _kt[_i + 1] == 0xFF:
        break
    KONKA_TECLAS.append((_kt[_i], _kt[_i + 1]))
assert len(KONKA_TECLAS) == 45 and len(_kt) == 46 * 4


def nombre_konka(t):
    # Misma numeración que los índices del LG; las que el LG no documenta, sin nombre (no se inventan).
    n = NOMBRES_LG.get(t)
    if n is None or "sin identificar" in n:
        return "tecla Konka 0x%02X (sin identificar)" % t
    return n


def verificar_tabla(bin_path, base_ram, addr, hex_esperado, nombre):
    """Si el original está en firmware_local, comprueba que la tabla embebida es la del firmware."""
    if not os.path.exists(bin_path):
        print("AVISO: falta", bin_path, "— la tabla de", nombre, "no se comprueba contra el firmware")
        return
    import zlib
    fw = open(bin_path, "rb").read()
    d = zlib.decompressobj(-15)
    app = fw[0x20080:0x23880] + d.decompress(fw[0x23880:])
    o = addr - (base_ram - 0x3800)
    assert app[o:o + len(hex_esperado) // 2].hex() == hex_esperado, "la tabla de %s no coincide" % nombre


verificar_tabla(os.path.join(RAIZ, "firmware_local", "originales", "TV4-32K16.bin"), kk.APP_BASE, 0x38FFA2,
                "0707" + KONKA_TABLA_HEX, "Konka")

guardar("konka-tv4-32k16-mirror.json", {
    "formato": 1,
    "id": "konka-tv4-32k16-mirror",
    "version": 4,
    "nombre": "Konka TV4-32K16 (TP.VST59S.PB813) — espejo (MIRROR), logo y mando",
    "descripcion": "Espejo H+V de imagen y menús. 'En el menú': opción MIRROR ON/OFF en el menú de fábrica "
                   "(en lugar de 'Logo'). 'Fijo': siempre activado (primera versión probada). "
                   "Logo de arranque: girado 180° (se dibuja antes de que actúe el espejo), propio o sin cambios.",
    "estado": "verificado",
    "original": {"sha256": kk.ORIG_SHA256},
    "app": {"baseRam": "0x%06X" % kk.APP_BASE},
    "compresion": {"metodo": "zopfli", "iteraciones": 5},
    "zonasPermitidas": [
        zona(0x21DA41, 0x21DA45, "XC init: bMirror"),
        zona(0x25E965, 0x25E969, "gancho 1 (tras cargar la base de datos)"),
        zona(0x2AE569, 0x2B1515, "lecturas de las banderas de espejo del OSD en la librería GOP"),
        zona(0x2B29A7, 0x2B29AB, "gancho 2 (creación del contexto GOP)"),
        zona(0x32A0B4, 0x32A0B8, "puntero de la etiqueta del menú"),
        zona(0x33AB66, 0x33B9A2, "textos de depuración JPEG sin referencias"),
        zona(0x38FFA2, 0x38FFA4 + 46 * 4, "código de cliente y lista código→tecla del mando (se copia a RAM al arrancar)"),
    ],
    "parchesFijos": [],
    "opciones": [
        {"id": "modo", "etiqueta": "Modo de espejo", "tipo": "lista", "porDefecto": "menu",
         "valores": [
             {"id": "menu", "etiqueta": "Opción MIRROR en el menú de fábrica", "parches": menu},
             {"id": "fijo", "etiqueta": "Espejo fijo (siempre activado)", "parches": fijo},
         ]},
        {"id": "logo", "etiqueta": "Logo de arranque", "tipo": "lista", "porDefecto": "girado",
         "valores": [
             {"id": "girado", "etiqueta": "Girado 180° (para el panel invertido)", "parches": [], "logo": "girar"},
             {"id": "original", "etiqueta": "Sin cambios", "parches": []},
             {"id": "propio", "etiqueta": "Propio (JPEG baseline 1366x768)", "parches": [], "logo": "propio"},
         ]},
    ],
    "logo": {"idTel": "0x065E", "longitudJpeg": kk.LOGO_JPEG_LEN, "ancho": 1366, "alto": 768},
    "clienteMando": {"direccion": "0x38FFA2", "formato": "bytes-invertidos", "original": "07 07"},
    "tablaMando": {
        "formato": "lista4",
        "direccion": "0x38FFA4",
        "entradas": 46,
        "original": KONKA_TABLA_HEX,
        "nota": "Máximo 45 teclas (detrás de la tabla hay otros datos). El mando de fábrica (cliente 88 90) usa otras "
                "tablas y no cambia. Solo se ofrecen las funciones que ya usa el mando original.",
        "funciones": [{"indice": "0x%02X" % t, "nombre": nombre_konka(t)} for t in sorted({t for _, t in KONKA_TECLAS})],
        "botones": [{"codigo": "0x%02X" % c, "nombre": "botón " + nombre_konka(t)} for c, t in KONKA_TECLAS],
    },
    "salidasConocidas": [
        {"opciones": {"modo": "menu", "logo": "girado"},
         "sha256": "81bd3c8b643745d321a99f3a6479dee8dc97b464d55bbb3be006fb6160b93570",
         "archivo": "TV4-32K16_MIRROR_MENU.bin"},
        {"opciones": {"modo": "menu", "logo": "original"},
         "sha256": "3bffdfd07255108fca0d1edc5cfa154f79e27375a3a4281d3284aa0d76750e59",
         "archivo": "TV4-32K16_MIRROR_MENU_SIN_LOGO.bin"},
        {"opciones": {"modo": "fijo", "logo": "original"},
         "sha256": "98a919fbef5f848147272fe37a6ccd51bbd0166c0eb22ae8a75125ef211e5fe0",
         "archivo": "TV4-32K16_MIRROR.bin"},
    ],
})

# Tabla código → función del ATEC (256 bytes en 0x422090; idéntica a la del Soyea en 0x423CE0).
ATEC_TABLA_HEX = (
    "000102030405060708090a0b0c0d0e0fffffffff14101112132627ff221dffff30191a1b1c6effff3c3dffffffffffff"
    "ffffffffffffffffffffffffffffffff2cffffffff2a2b3aff2fff2523ff1e58ff66ff211f207270ffffffffffffffff"
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
    "ffffffffffffffffffffffffffffffff"
)

# ------------------------------------------------------------------ familia BD_MST061B_10AZU_14014
# ATEC 32L18D y la placa LM2SA V1.6 (dos firmwares). Mismo programa base: misma tabla de paneles (5 entradas,
# la #1 WXGA_AU20_T200XW02 1366x768), misma rutina IR (cliente 36 77 con bg.sfeqi r7), misma inicialización del
# receptor IR (+0x577/+0x582 desde la comparación), misma entrada en reposo en el código de arranque sin comprimir
# (RAM 0x243115 / 0x243120), tabla código→función directa de 256 bytes y logo en el recurso TEL 0x0300.
# Ver docs/historial/README_ATEC_*.md y README_LM2SA_PERFILES.md.
at = cargar("patch_panel.py")
import hashlib
import math
import struct
import zlib


def app_de(fw):
    fin = struct.unpack_from("<I", fw, 0x2000C)[0]
    return fw[0x20080:0x23880] + zlib.decompressobj(-15).decompress(fw[0x23880:fin])


def tiempos(hsync, hbp, vsync, vbp, w, h, ht, vt, mhz, rangos=None):
    # Resolución: tiempos de fuentes publicadas (no inventados). Márgenes mín./máx.: regla fija documentada
    # (los estándares no los dan): HTotal −5 %/+5 % (y porche delantero ≥ 16), VTotal −2 %/+3 %, DCLK −7 %/+7 %.
    t = dict(hsync=hsync, hbp=hbp, vsync=vsync, vbp=vbp, hstart=hsync + hbp, vstart=vsync + vbp,
             width=w, height=h, htotal=ht, vtotal=vt, dclk=round(mhz))
    if rangos:
        t.update(rangos)
    else:
        t.update(htotal_min=max(round(ht * 0.95), w + hsync + hbp + 16), htotal_max=round(ht * 1.05),
                 vtotal_min=max(round(vt * 0.98), h + vsync + vbp + 1), vtotal_max=round(vt * 1.03),
                 dclk_min=math.floor(mhz * 0.93), dclk_max=math.ceil(mhz * 1.07))
    return t


L = at.LP141


def resoluciones(etiqueta_1280x800):
    return [
        # id, etiqueta, tiempos, origen
        ("1280x800", etiqueta_1280x800,
         tiempos(L["hsync"], L["hbp"], L["vsync"], L["vbp"], 1280, 800, L["htotal"], L["vtotal"], L["dclk"],
                 {k: L[k] for k in ("htotal_min", "htotal_max", "vtotal_min", "vtotal_max", "dclk_min", "dclk_max")}),
         "hoja de datos LP141WX1"),
        ("1280x720", "1280x720 — tiempos estándar 720p (CEA-861) — sin probar",
         tiempos(40, 220, 5, 20, 1280, 720, 1650, 750, 74.25), "CEA-861 720p60: 74,25 MHz, 1650x750"),
        ("1024x768", "1024x768 — tiempos estándar VESA DMT — sin probar",
         tiempos(136, 160, 6, 29, 1024, 768, 1344, 806, 65.0), "VESA DMT 1024x768@60: 65 MHz, 1344x806"),
        ("800x600", "800x600 — tiempos estándar VESA DMT — sin probar",
         tiempos(128, 88, 4, 23, 800, 600, 1056, 628, 40.0), "VESA DMT 800x600@60: 40 MHz, 1056x628"),
    ]


def entrada(orig_entry, t):
    e = bytearray(orig_entry)
    e[20], e[21], e[22], e[23] = t["hsync"], t["hbp"], t["vsync"], t["vbp"]
    struct.pack_into("<HHHH", e, 24, t["hstart"], t["vstart"], t["width"], t["height"])
    struct.pack_into("<HHH", e, 32, t["htotal_max"], t["htotal"], t["htotal_min"])
    struct.pack_into("<HHH", e, 38, t["vtotal_max"], t["vtotal"], t["vtotal_min"])
    e[44], e[45], e[46] = t["dclk_max"], t["dclk"], t["dclk_min"]
    struct.pack_into("<II", e, 76, round(at.SET_K / t["dclk_min"]), round(at.SET_K / t["dclk_max"]))
    return bytes(e)


def entrada_8bits(orig_entry):
    # 8 bits: sin dither, TiBitMode 2 (el de los paneles de 8 bits de la tabla), OutputFormat 0 (el del original).
    e = bytearray(orig_entry)
    e[4] &= ~0x01 & 0xFF
    struct.pack_into("<II", e, 64, 2, 0)
    return bytes(e)


def entrada_6bits(orig_entry):
    # Lo que cambia patch_entry(six_bit=True) fuera de los tiempos: dither en +4; TiBitMode/OutputFormat en +64.
    e = bytearray(orig_entry)
    e[4] |= 0x01                                   # m_bPanelDither = 1
    struct.pack_into("<II", e, 64, 3, 1)
    return bytes(e)


def perfil_familia_061b(fw, c):
    """c: id, version, nombre, descripcion, estado, panel (RAM de la entrada #1), ir (RAM del bg.sfeqi),
    tabla (RAM de la tabla código→función), compresion, logo, etiquetas, salidas."""
    assert hashlib.sha256(fw).hexdigest() == c["sha256"]
    app = app_de(fw)
    E = c["panel"]
    o = E - 0x240000                               # la app empieza en RAM 0x240000 (base 0x243800 - 0x3800)
    orig_entry = bytes(app[o:o + at.ENTRY_SIZE])
    f = at.entry_fields(app, o)
    assert (f["width"], f["height"], f["htotal"], f["vtotal"], f["dclk"]) == (1366, 768, 1560, 806, 76)
    nombre = app[struct.unpack_from("<I", orig_entry, 0)[0] - 0x240000:][:18]
    assert nombre == b"WXGA_AU20_T200XW02", nombre
    RES = resoluciones(c["etiqueta1280x800"])
    if c.get("comprobar_patch_panel"):
        # Con los tiempos del LP141WX1 sale la misma entrada que patch_panel.py (8 y 6 bits).
        # (patch_panel.py trabaja sobre la app sin los 0x3800 bytes sin comprimir: offset PANEL_ENTRY.)
        app2, _, _ = at.extract_app(fw)
        o2 = at.PANEL_ENTRY
        assert o2 == o - 0x3800 and bytes(app2[o2:o2 + at.ENTRY_SIZE]) == orig_entry
        a8 = bytearray(app2)
        at.patch_entry(a8, False)
        assert entrada(orig_entry, RES[0][2]) == bytes(a8[o2:o2 + at.ENTRY_SIZE])
        a6 = bytearray(app2)
        at.patch_entry(a6, True)
        e6 = bytes(a6[o2:o2 + at.ENTRY_SIZE])
        assert [i for i in range(at.ENTRY_SIZE) if e6[i] != bytes(a8[o2:o2 + at.ENTRY_SIZE])[i]] == [4, 64, 68]
        m6 = entrada_6bits(orig_entry)
        assert m6[4:6] == e6[4:6] and m6[64:72] == e6[64:72], (m6[4:6].hex(), e6[4:6].hex(), m6[64:72].hex(), e6[64:72].hex())
    e6 = entrada_6bits(orig_entry)

    valores_res = [{"id": "original", "etiqueta": "1366x768 — original (panel T200XW02)", "parches": []}]
    for rid, etq, t, fuente in RES:
        e = entrada(orig_entry, t)
        assert all(e[i] == orig_entry[i] for i in range(at.ENTRY_SIZE) if not (20 <= i <= 46 or 76 <= i <= 83))
        d = ("Panel %dx%d (%s): sincronismos/porches, inicio, tamaño, totales %d/%d/%d x %d/%d/%d, DCLK %d/%d/%d MHz"
             % (t["width"], t["height"], fuente, t["htotal_min"], t["htotal"], t["htotal_max"],
                t["vtotal_min"], t["vtotal"], t["vtotal_max"], t["dclk_min"], t["dclk"], t["dclk_max"]))
        valores_res.append({"id": rid, "etiqueta": etq, "parches": [
            parche(E + 20, orig_entry[20:47].hex(), e[20:47].hex(), d),
            parche(E + 76, orig_entry[76:84].hex(), e[76:84].hex(), "Límites del PLL (MaxSET/MinSET) para ese DCLK"),
        ]})
    bits6 = [parche(E + 4, orig_entry[4:6].hex(), e6[4:6].hex(), "m_bPanelDither = 1 (dither para 6 bits)"),
             parche(E + 64, orig_entry[64:72].hex(), e6[64:72].hex(), "m_ucTiBitMode = 3 y m_ucOutputFormatBitMode = 1 (6 bits)")]
    # 8 bits: solo los bytes que difieran del original (en los firmwares conocidos el original ya es de 8 bits:
    # TiBitMode 2, OutputFormat 0, sin dither → la opción no cambia nada y da el mismo archivo que "Sin cambios").
    e8 = entrada_8bits(orig_entry)
    bits8 = []
    if e8[4:6] != orig_entry[4:6]:
        bits8.append(parche(E + 4, orig_entry[4:6].hex(), e8[4:6].hex(), "m_bPanelDither = 0 (8 bits)"))
    if e8[64:72] != orig_entry[64:72]:
        bits8.append(parche(E + 64, orig_entry[64:72].hex(), e8[64:72].hex(), "m_ucTiBitMode = 2 y m_ucOutputFormatBitMode = 0 (8 bits)"))
    original_8 = not bits8
    etq_orig = "Sin cambios — la que trae el firmware" + (" (8 bits)" if original_8 else "")
    etq_8 = c["etiquetaBits8"] + (" — en este firmware es lo mismo que «Sin cambios»" if original_8 else "")

    # Mando: comparación del cliente, inicialización del receptor IR (reposo) y entrada en reposo (stub).
    ir = c["ir"]
    r1, r2 = ir + 0x577, ir + 0x582
    stub = lambda ram: 0x23880 - (0x243800 - ram)
    assert app[ir - 0x240000:ir - 0x240000 + 4].hex() == "c0eee6c0"           # bg.sfeqi r7,0x7736
    assert app[r1 - 0x240000 - 4:r1 - 0x240000 + 3].hex() == "c8603da4508036"   # ori r3,0x3da4 ; ori r4,0x36
    assert app[r2 - 0x240000 - 4:r2 - 0x240000 + 3].hex() == "c8603da5508077"   # ori r3,0x3da5 ; ori r4,0x77
    assert fw[stub(0x243115):stub(0x243115) + 3].hex() == "50e036" and fw[stub(0x243120):stub(0x243120) + 3].hex() == "50e077"
    soyea_main = [
        parche(ir, "c0eee6c0", "c0ef06e0",
               "IR: bg.sfeqi r7,0x7736 -> bg.sfeqi r7,0x7837 (código de cliente del mando Soyea)"),
        parche(r1, "508036", "508037", "IR reposo: bn.ori r4,r0,0x36 -> 0x37 (registro 0x3DA4, 1.er byte del cliente)"),
        parche(r2, "508077", "508078", "IR reposo: bn.ori r4,r0,0x77 -> 0x78 (registro 0x3DA5, 2.º byte del cliente)"),
        # v6: la rutina de entrada en reposo (0x2430F1, "=> StandBy") vuelve a escribir el código fijo antes de
        # apagar. Está en el código de arranque SIN comprimir (flash 0x23195 / 0x231A0). Probado por el usuario.
        parche(0x243115, "50e036", "50e037", "Entrada en reposo: bn.ori r7,r0,0x36 -> 0x37 (RIU 0x7B48 = IR 0x3DA4, byte bajo)"),
        parche(0x243120, "50e077", "50e078", "Entrada en reposo: bn.ori r7,r0,0x77 -> 0x78 (RIU 0x7B49 = IR 0x3DA4, byte alto)"),
    ]
    T = c["tabla"]
    tabla = app[T - 0x240000:T - 0x240000 + 256]
    tabla_hex = tabla.hex()
    funciones = {tabla[k]: BOTONES_SOYEA_D[k] for k in range(256) if tabla[k] != 0xFF}
    assert len(funciones) == 48

    lg = c["logo"]
    res = fw[lg["offset"]:lg["offset"] + lg["recurso"]]
    assert res[:2] == b"\xff\xd8" and res[lg["longitudJpeg"] - 2:lg["longitudJpeg"]] == b"\xff\xd9"
    guardar(c["archivo"], {
        "formato": 1,
        "id": c["id"],
        "version": c["version"],
        "nombre": c["nombre"],
        "descripcion": c["descripcion"],
        "estado": c["estado"],
        "original": {"sha256": c["sha256"]},
        "app": {"baseRam": "0x243800"},
        "compresion": c["compresion"],
        "zonasPermitidas": [
            zona(0x243115, 0x243123, "entrada en reposo (0x2430F1): código de cliente para despertar; código de arranque sin comprimir"),
            zona(ir, ir + 4, "comparación del código de cliente del mando (rutina IR 0x%06X)" % (ir - 0x2B)),
            zona(r1, r2 + 3, "código de cliente para encender desde reposo (registros IR 0x3DA4/0x3DA5)"),
            zona(E, E + at.ENTRY_SIZE, "entrada #1 de la tabla de paneles (la que usa este TV)"),
            zona(T, T + 0x100, "tabla código→función del mando (256 bytes; rutina IR 0x%06X)" % (ir - 0x2B)),
        ],
        "parchesFijos": [],
        "opciones": [
            {"id": "resolucion", "etiqueta": "Resolución del panel", "tipo": "lista", "porDefecto": "original",
             "valores": valores_res},
            {"id": "bits", "etiqueta": "Salida LVDS", "tipo": "lista", "porDefecto": "original",
             "valores": [
                 {"id": "original", "etiqueta": etq_orig, "parches": []},
                 {"id": "8", "etiqueta": etq_8, "parches": bits8},
                 {"id": "6", "etiqueta": c["etiquetaBits6"], "parches": bits6},
             ]},
            # La rutina IR (0x3233AA en el ATEC) es la del Soyea (0x32519F) y su tabla código→función es la del
            # Soyea (0x423CE0): solo cambia el código de cliente (en 5 sitios).
            {"id": "mando", "etiqueta": "Mando", "tipo": "lista", "porDefecto": "atec",
             "valores": [
                 {"id": "atec", "etiqueta": "ATEC original (cliente 36 77)", "parches": []},
                 {"id": "soyea", "etiqueta": c["etiquetaSoyea"], "parches": soyea_main},
             ]},
            # Logo: el programa (0x2E350F en el ATEC) carga SIEMPRE el recurso TEL 0x0300; la búsqueda de recursos
            # (0x322A4A) no comprueba el CRC-16, pero se mantiene correcto.
            {"id": "logo", "etiqueta": "Logo de arranque", "tipo": "lista", "porDefecto": "original",
             "valores": [
                 {"id": "original", "etiqueta": "Sin cambios", "parches": []},
                 {"id": "girado", "etiqueta": "Girado 180° — sin probar en el TV", "parches": [], "logo": "girar"},
                 {"id": "propio", "etiqueta": "Propio (imagen; se ajusta a %dx%d) — sin probar en el TV"
                                              % (lg["ancho"], lg["alto"]), "parches": [], "logo": "propio"},
             ]},
        ],
        "logo": {"idTel": "0x0300", "crc16": True, "longitudJpeg": lg["longitudJpeg"], "ancho": lg["ancho"], "alto": lg["alto"]},
        # Código de cliente: inmediato con signo de bg.sfeqi r7 (solo hasta 0x7FFF) + 4 copias para el reposo
        # (inicialización del receptor IR y entrada en reposo). El segundo mando aceptado (A0 5F) no se toca.
        "clienteMando": {"direccion": "0x%06X" % ir, "formato": "aeon-sfeqi", "registro": 7, "original": "36 77",
                         "reposo": [{"direccion": "0x%06X" % r1, "byte": 1, "registro": 4},
                                    {"direccion": "0x%06X" % r2, "byte": 2, "registro": 4},
                                    {"direccion": "0x243115", "byte": 1, "registro": 7},
                                    {"direccion": "0x243120", "byte": 2, "registro": 7}]},
        "tablaMando": {
            "formato": "directa256",
            "direccion": "0x%06X" % T,
            "original": tabla_hex,
            "nota": "Las funciones se llaman como el botón del mando Soyea/ATEC que las produce. La cruceta de ese "
                    "mando envía VOL±/CH±; dentro de los menús el firmware (el mismo del Soyea) las usa como flechas.",
            "funciones": [{"indice": "0x%02X" % t, "nombre": n} for t, n in sorted(funciones.items())],
            "botones": [{"codigo": "0x%02X" % k, "nombre": n} for k, n in sorted(BOTONES_SOYEA_D.items())],
        },
        # Las salidas documentadas con bits = 8 son también las de "Sin cambios" (mismo archivo).
        "salidasConocidas": c["salidas"] + ([dict(k, opciones=dict(k["opciones"], bits="original"))
                                             for k in c["salidas"] if k["opciones"].get("bits") == "8"] if original_8 else []),
    })
    return tabla_hex


BOTONES_SOYEA_D = dict(BOTONES_SOYEA)
ORIG = os.path.join(RAIZ, "firmware_local", "originales")

# ------------------------------------------------------------------ ATEC 32L18D
atec_bin = os.path.join(ORIG, "Atec_32_KPC_Salva.BIN")
if not os.path.exists(atec_bin):
    print("AVISO: falta", atec_bin, "— no se genera el perfil ATEC")
else:
    t = perfil_familia_061b(open(atec_bin, "rb").read(), {
        "archivo": "atec-32l18d-lp141wx1.json", "id": "atec-32l18d-lp141wx1", "version": 6,
        "sha256": at.ORIG_SHA256, "comprobar_patch_panel": True,
        "nombre": "ATEC 32L18D (BD_MST061B_10AZU_14014) — resolución del panel, mando y logo",
        "descripcion": "Resolución del panel (1366x768 original o menor) y salida LVDS de 8/6 bits; mando Soyea "
                       "(también para encender desde reposo) o código propio; logo. El LP141WX1 1280x800 a 8 bits "
                       "funcionó en el TV (mapeo JEIDA; el par 3 queda sin conectar); a 6 bits + dither NO funcionó. "
                       "Las demás resoluciones usan tiempos estándar: comprueba que tu panel los admite (hoja de datos). "
                       "Espejo de imagen y menús: ya existe en el menú de fábrica (Mirror Enable).",
        "estado": "verificado",
        "panel": 0x4E3A78, "ir": 0x3233D5, "tabla": 0x422090,
        "compresion": {"metodo": "zlib", "nivel": 9},
        "logo": {"offset": 0x2D5010, "recurso": 49136, "longitudJpeg": 49128, "ancho": 2167, "alto": 1228},
        "etiqueta1280x800": "1280x800 — panel LP141WX1 (probado: FUNCIONÓ)",
        "etiquetaBits8": "8 bits (con LP141WX1 FUNCIONÓ)",
        "etiquetaBits6": "6 bits + dither (con LP141WX1 NO funcionó)",
        "etiquetaSoyea": "Soyea LKK32CMB (cliente 37 78), también enciende desde reposo (probado: FUNCIONÓ)",
        "salidas": [
            {"opciones": {"resolucion": "1280x800", "bits": "8", "mando": "atec", "logo": "original"},
             "sha256": "ab3d3485707e3c271e129ae9be633ad6ca3d63aa2051c5ef16596dbc727c918c",
             "archivo": "Atec32L18D_LP141WX1_8bit.BIN"},
            {"opciones": {"resolucion": "1280x800", "bits": "6", "mando": "atec", "logo": "original"},
             "sha256": "7bd55c392efabf11ad37b61eff631792ba285bb82a1ce71d06bdee89976d65e2",
             "archivo": "Atec32L18D_LP141WX1_6bit.BIN"},
            {"opciones": {"resolucion": "1280x800", "bits": "8", "mando": "soyea", "logo": "original"},
             "sha256": "8e797d5dbb8056ec36630b3653eb194a35e08a515e86d404f6a4706ef92aaecd",
             "archivo": "ATEC_8bit_mando_Soyea_PRUEBA_reposo.BIN"},
        ],
    })
    assert t == ATEC_TABLA_HEX

# ------------------------------------------------------------------ placa LM2SA V1.6 (PA20406-2-36ZG)
# Dos firmwares del usuario con el ID de placa del ATEC; análisis en docs/historial/README_LM2SA_PERFILES.md.
COMUN_LM2SA = {
    # "verificado" = parches comprobados contra el binario real (no que esté probado en el TV; eso lo dicen las etiquetas).
    "estado": "verificado",
    "compresion": {"metodo": "zopfli", "iteraciones": 5},   # zlib -9 no cabe (el original deja 6 / 1 bytes libres)
    "etiqueta1280x800": "1280x800 — panel LP141WX1 (misma entrada que la probada en el ATEC; sin probar en esta placa)",
    "etiquetaBits8": "8 bits",
    "etiquetaBits6": "6 bits + dither (en el ATEC con LP141WX1 NO funcionó)",
    "etiquetaSoyea": "Soyea LKK32CMB (cliente 37 78), también enciende desde reposo (mismos cambios que el ATEC; sin probar en esta placa)",
    "salidas": [],
}
DESC_LM2SA = ("Mismo programa base que el ATEC 32L18D (ID de placa BD_MST061B_10AZU_14014): resolución del panel "
              "(1366x768 original o menor) y salida LVDS de 8/6 bits; mando Soyea (también para encender desde "
              "reposo) o código propio; logo (720x480). Las demás resoluciones usan tiempos estándar: comprueba que "
              "tu panel los admite (hoja de datos). Espejo de imagen y menús: ya existe en el menú de fábrica "
              "(Mirror Enable). Nada de este perfil está probado todavía en esta placa.")
for archivo_bin, cfg in (
        ("MST_EXT_SPI_ISP_20210531_113958.BIN", {
            "archivo": "lm2sa-v16-20210531.json", "id": "lm2sa-v16-20210531", "version": 1,
            "sha256": "d0d2b24f4bda26564c62f7c8e1747dbdcddfc500b2ab73b796853be4913cf239",
            "nombre": "Placa LM2SA V1.6 — firmware 20210531 (programa del 16-04-2015) — resolución, mando y logo",
            "panel": 0x4D106C, "ir": 0x31EAC1, "tabla": 0x4131F0,
            "logo": {"offset": 0x2C86A0, "recurso": 55088, "longitudJpeg": 55079, "ancho": 720, "alto": 480}}),
        ("MST_EXT_SPI_ISP_20210614_165953.BIN", {
            "archivo": "lm2sa-v16-20210614.json", "id": "lm2sa-v16-20210614", "version": 1,
            "sha256": "aa8b1502e7d88faa0c77f7cef86a5fbf739261846ebd94e02b891e53a978b421",
            "nombre": "Placa LM2SA V1.6 — firmware 20210614 (programa del 22-05-2014) — resolución, mando y logo",
            "panel": 0x4BB06C, "ir": 0x31E049, "tabla": 0x412460,
            "logo": {"offset": 0x2C0440, "recurso": 55088, "longitudJpeg": 55079, "ancho": 720, "alto": 480}}),
):
    ruta = os.path.join(ORIG, archivo_bin)
    if not os.path.exists(ruta):
        print("AVISO: falta", ruta, "— no se genera", cfg["id"])
        continue
    perfil_familia_061b(open(ruta, "rb").read(), dict(COMUN_LM2SA, descripcion=DESC_LM2SA, **cfg))

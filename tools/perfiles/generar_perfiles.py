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
    "version": 6,
    "nombre": "LG 42LF652VX (TP.VST56.PB801) — mando Soyea LKK32CMB y logo",
    "descripcion": "Obedecer al mando Soyea LKK32CMB con la misma función por botón que el firmware Soyea; "
                   "cruceta como flechas en menús y USB (v5). Opción de desactivar el espejo de fábrica. "
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
        {"id": "espejo", "etiqueta": "Espejo de imagen", "tipo": "lista", "porDefecto": "original",
         "valores": [
             {"id": "original", "etiqueta": "Como el original (activado)", "parches": []},
             {"id": "off", "etiqueta": "Desactivado", "parches": [p_espejo]},
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
    "tablaMando": {
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

guardar("konka-tv4-32k16-mirror.json", {
    "formato": 1,
    "id": "konka-tv4-32k16-mirror",
    "version": 3,
    "nombre": "Konka TV4-32K16 (TP.VST59S.PB813) — espejo (MIRROR) y logo",
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

# ------------------------------------------------------------------ ATEC 32L18D
at = cargar("patch_panel.py")
atec_bin = os.path.join(RAIZ, "firmware_local", "originales", "Atec_32_KPC_Salva.BIN")
if not os.path.exists(atec_bin):
    print("AVISO: falta", atec_bin, "— no se genera el perfil ATEC")
else:
    fw = open(atec_bin, "rb").read()
    import hashlib
    assert hashlib.sha256(fw).hexdigest() == at.ORIG_SHA256
    app, _, _ = at.extract_app(fw)
    base = at.APP_RAM_DEST + 0x3800
    o = at.PANEL_ENTRY
    orig_entry = bytes(app[o:o + at.ENTRY_SIZE])
    variantes = {}
    for six in (False, True):
        a = bytearray(app)
        at.patch_entry(a, six)
        variantes[six] = bytes(a[o:o + at.ENTRY_SIZE])
    desc = ("Entrada del panel 'WXGA_AU20_T200XW02' → LP141WX1 1280x800 (porches, totales, DCLK %d–%d MHz, "
            "límites del PLL)" % (at.LP141["dclk_min"], at.LP141["dclk_max"]))
    guardar("atec-32l18d-lp141wx1.json", {
        "formato": 1,
        "id": "atec-32l18d-lp141wx1",
        "version": 3,
        "nombre": "ATEC 32L18D (BD_MST061B_10AZU_14014) — panel LP141WX1 1280x800, mando y logo",
        "descripcion": "Adapta la tabla del panel a 1280x800 un canal LVDS. La salida de 8 bits funcionó en el TV "
                       "(necesita mapeo JEIDA; el par 3 queda sin conectar); la de 6 bits + dither NO funcionó.",
        "estado": "verificado",
        "original": {"sha256": at.ORIG_SHA256},
        "app": {"baseRam": "0x%06X" % base},
        "compresion": {"metodo": "zlib", "nivel": 9},
        "zonasPermitidas": [
            zona(0x3233D5, 0x3233D9, "comparación del código de cliente del mando (rutina IR 0x3233AA)"),
            zona(base + o, base + o + at.ENTRY_SIZE, "entrada #1 de la tabla de paneles (la que usa este TV)"),
        ],
        "parchesFijos": [],
        "opciones": [
            {"id": "bits", "etiqueta": "Salida LVDS", "tipo": "lista", "porDefecto": "8",
             "valores": [
                 {"id": "8", "etiqueta": "8 bits (probado: FUNCIONÓ)",
                  "parches": [parche(base + o, orig_entry.hex(), variantes[False].hex(), desc + ", 8 bits")]},
                 {"id": "6", "etiqueta": "6 bits + dither (probado: NO funcionó)",
                  "parches": [parche(base + o, orig_entry.hex(), variantes[True].hex(),
                                     desc + ", 6 bits (TiBitMode 3, OutputFormat 1, dither)")]},
             ]},
            # La rutina IR del ATEC (0x3233AA) es la del Soyea (0x32519F) y su tabla código→función
            # (0x422090) es idéntica a la del Soyea (0x423CE0): solo cambia el código de cliente.
            {"id": "mando", "etiqueta": "Mando", "tipo": "lista", "porDefecto": "atec",
             "valores": [
                 {"id": "atec", "etiqueta": "ATEC original (cliente 0x7736)", "parches": []},
                 {"id": "soyea", "etiqueta": "Soyea LKK32CMB (cliente 0x7837) — sin probar en el TV",
                  "parches": [parche(0x3233D5, "c0eee6c0", "c0ef06e0",
                                     "IR: bg.sfeqi r7,0x7736 -> bg.sfeqi r7,0x7837 (código de cliente del mando Soyea)")]},
             ]},
            # Logo: el programa (0x2E350F) carga SIEMPRE el recurso TEL 0x0300; la búsqueda de recursos
            # (0x322A4A) no comprueba el CRC-16, pero se mantiene correcto.
            {"id": "logo", "etiqueta": "Logo de arranque", "tipo": "lista", "porDefecto": "original",
             "valores": [
                 {"id": "original", "etiqueta": "Sin cambios", "parches": []},
                 {"id": "girado", "etiqueta": "Girado 180° — sin probar en el TV", "parches": [], "logo": "girar"},
                 {"id": "propio", "etiqueta": "Propio (imagen; se ajusta a 2167x1228) — sin probar en el TV",
                  "parches": [], "logo": "propio"},
             ]},
        ],
        "logo": {"idTel": "0x0300", "crc16": True, "longitudJpeg": 49128, "ancho": 2167, "alto": 1228},
        "salidasConocidas": [
            {"opciones": {"bits": "8", "mando": "atec", "logo": "original"},
             "sha256": "ab3d3485707e3c271e129ae9be633ad6ca3d63aa2051c5ef16596dbc727c918c",
             "archivo": "Atec32L18D_LP141WX1_8bit.BIN"},
            {"opciones": {"bits": "6", "mando": "atec", "logo": "original"},
             "sha256": "7bd55c392efabf11ad37b61eff631792ba285bb82a1ce71d06bdee89976d65e2",
             "archivo": "Atec32L18D_LP141WX1_6bit.BIN"},
        ],
    })

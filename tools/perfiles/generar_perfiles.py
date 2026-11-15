#!/usr/bin/env python3
"""Genera los perfiles JSON integrados a partir de las constantes de los scripts de referencia
(tools/referencia_python). Así los bytes de los parches no se copian a mano.

Uso:  python3 tools/perfiles/generar_perfiles.py
Salida: src/FirmReaper.Core/Profiles/Builtin/*.json
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
guardar("lg-42lf652vx-soyea.json", {
    "formato": 1,
    "id": "lg-42lf652vx-soyea",
    "version": 5,
    "nombre": "LG 42LF652VX (TP.VST56.PB801) — mando Soyea LKK32CMB",
    "descripcion": "Obedecer al mando Soyea LKK32CMB con la misma función por botón que el firmware Soyea; "
                   "cruceta como flechas en menús y USB (v5). Opción de desactivar el espejo de fábrica.",
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
    ],
    "salidasConocidas": [
        {"opciones": {"mando": "si", "espejo": "original"},
         "sha256": "17918f8fa6607bb802fdf6c2e71f554b547a390201eb65b9e704494b9fd9b002",
         "archivo": "LG_42LF652VX_MANDO_SOYEA_v5.BIN"},
        {"opciones": {"mando": "si", "espejo": "off"},
         "sha256": "3a836aee3dacb7d60fade3f7e834e8578309156e58a02e365e122fe236fed599",
         "archivo": "LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN"},
    ],
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

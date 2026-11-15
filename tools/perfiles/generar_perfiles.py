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
guardar("konka-tv4-32k16-mirror.json", {
    "formato": 1,
    "id": "konka-tv4-32k16-mirror",
    "version": 1,
    "nombre": "Konka TV4-32K16 (TP.VST59S.PB813) — MIRROR en el menú de fábrica",
    "descripcion": "Espejo H+V de imagen y menús, activable en el menú de fábrica (opción 'Logo' → 'MIRROR'). "
                   "El logo girado 180° se añade en la etapa 3.",
    "estado": "sin-verificar",
    "original": {"sha256": kk.ORIG_SHA256},
    "app": {"baseRam": "0x%06X" % kk.APP_BASE},
    "compresion": {"metodo": "zopfli", "iteraciones": 5},
    "zonasPermitidas": [
        zona(0x21DA41, 0x21DA45, "XC init: bMirror"),
        zona(0x25E965, 0x25E969, "gancho 1 (tras cargar la base de datos)"),
        zona(0x2B29A7, 0x2B29AB, "gancho 2 (creación del contexto GOP)"),
        zona(0x32A0B4, 0x32A0B8, "puntero de la etiqueta del menú"),
        zona(0x33AB66, 0x33B9A2, "textos de depuración JPEG sin referencias"),
    ],
    "parchesFijos": [parche(*x) for x in kk.PATCHES],
    "opciones": [],
    "salidasConocidas": [],
})

# Pruebas

```bash
dotnet test --project tests/FirmReaper.Core.Tests
```

Usa xUnit v3 con Microsoft Testing Platform (activado en `global.json`).

## Qué se prueba

| Archivo | Pruebas |
|---|---|
| `Crc32MsbTests` | Vectores de referencia sacados de `crc32_nr()` de Python; cálculo incremental |
| `RawInflaterTests` | Igual que `DeflateStream` en varios tamaños/niveles; posición final con datos detrás; truncado; límite de salida; bloque inválido; 2 000 entradas aleatorias |
| `AnalyzerSyntheticTests` | Imagen MStar **sintética** (sin datos del fabricante): válida; CRC, cola, fin en flash, TEL, solapamiento y offsets corruptos; no modifica la entrada |
| `CatalogTests` | Catálogo incrustado válido; 10 tipos de catálogo malicioso rechazados |
| `RealFirmwareTests` | 11 firmwares reales: huella, fin de flash, CRC y tamaño de app iguales a Python; 1 bit cambiado → inválido; cambio fuera del CRC → "desconocido válido"; Zopfli recomprime la app del LG v5 igual; el perfil LG genera las 2 versiones v5 idénticas; solo espejo = 1 byte; Konka: menú+logo girado = `81bd3c8b…`, menú sin logo, fijo; girar el logo = JPEG de Pillow; logo propio demasiado grande |
| `ZopfliTests` | 7 vectores de py-zopfli idénticos; símbolos de distancia; `ExactLog` = glibc en 1..2 100 000; constantes de ln 2 |
| `ProfileTests` | Perfiles integrados válidos y coherentes con el catálogo; 22 perfiles manipulados rechazados; carpeta externa |
| `LogoTests` | Codificador → JPEG válido; girar conserva tablas; logos inválidos rechazados (dimensiones, progresivo, basura final, truncado); 3 000 JPEG corruptos sin excepciones inesperadas; reglas de `logo` en perfiles |
| `BuilderTests` | Generación sintética; no modifica la entrada; huella/bytes distintos; ya aplicado; no cabe; base incorrecta; guardado sin sobrescribir, con copia y sin temporales; original cambiado en disco |

## Firmwares reales

No se suben a git. Las pruebas los buscan en `FIRMREAPER_FW_DIR` o en `firmware_local/` (subcarpetas
`originales/` y `referencia/`). Si faltan, esas pruebas se **omiten** (no fallan).

| Archivo | SHA-256 | Origen |
|---|---|---|
| `Atec_32_KPC_Salva.BIN` | `19491133…bad647b` | original |
| `MST_EXT_SPI_ISP_20220511_165639.BIN` | `ef5c8055…36bee871` | original LG |
| `MST_EXT_SPI_ISP_20230301_153737.BIN` | `fc9fed8f…b600a21e` | Soyea (referencia) |
| `LG_42LF652VX_MANDO_SOYEA_v5.BIN` | `17918f8f…9fd9b002` | `patch_ir_lg.py` |
| `LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN` | `3a836aee…36fed599` | `patch_ir_lg.py --espejo-off` |
| `Atec32L18D_LP141WX1_6bit.BIN` | `7bd55c39…89976d65e2` | `patch_panel.py` |
| `Atec32L18D_LP141WX1_8bit.BIN` | `ab3d3485…dc727c918c` | `patch_panel.py` |
| `TV4-32K16.bin` | `32c52965…dcadaad9e` | original Konka |
| `TV4-32K16_MIRROR_MENU.bin` | `81bd3c8b…160b93570` | `patch_mirror_menu.py` (con logo) |
| `TV4-32K16_MIRROR.bin` | `98a919fb…211e5fe0` | versión "espejo fijo" (recibida) |
| `TV4-32K16_MIRROR_MENU_SIN_LOGO.bin` | `3bffdfd0…d76750e59` | `patch_mirror_menu.py --sin-logo` |

Las salidas se regeneran con los scripts de `tools/referencia_python` (Python 3, `pip install zopfli`;
zlib 1.3, zopfli 0.4.3):

```bash
cd firmware_local/referencia
```
```bash
python3 ../../tools/referencia_python/patch_ir_lg.py ../originales/MST_EXT_SPI_ISP_20220511_165639.BIN
```
```bash
python3 ../../tools/referencia_python/patch_ir_lg.py ../originales/MST_EXT_SPI_ISP_20220511_165639.BIN --espejo-off
```
```bash
python3 ../../tools/referencia_python/patch_panel.py ../originales/Atec_32_KPC_Salva.BIN
```
```bash
python3 ../../tools/referencia_python/patch_mirror_menu.py ../originales/TV4-32K16.bin --sin-logo
```
(renombrar la salida a `TV4-32K16_MIRROR_MENU_SIN_LOGO.bin`; después, sin `--sin-logo`, sale la versión con logo.
El logo depende de Pillow: se generó con Pillow 12.3.0 / libjpeg-turbo 3.1.4.1.)

Vectores de Zopfli (Python, `pip install zopfli==0.4.3`): ver el generador en el comentario de
`ZopfliTests.Gen`; huella del logaritmo: `math.log(i)` empaquetado `'<d'` para i = 1..2 100 000.

## Resultado actual

122 pruebas, 122 superadas (con firmwares, Linux). En GitHub Actions (Windows, sin firmwares) se omiten las
que necesitan los `.BIN`.

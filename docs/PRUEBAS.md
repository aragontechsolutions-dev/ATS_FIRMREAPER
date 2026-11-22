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
| `RealFirmwareTests` | 11 firmwares reales: huella, fin de flash, CRC y tamaño de app iguales a Python; 1 bit cambiado → inválido; cambio fuera del CRC → "desconocido válido"; Zopfli recomprime la app del LG v5 igual; el perfil LG genera las 2 versiones v5 idénticas; solo espejo = 1 byte; Konka: menú+logo girado = `81bd3c8b…`, menú sin logo, fijo; girar el logo = JPEG de Pillow; logo propio demasiado grande; LG con tabla editada (solo cambia 1 byte frente a la v5); zlib recomprime la app del ATEC igual; perfil ATEC 8/6 bits idéntico a `patch_panel.py`; ATEC con mando Soyea (solo cambia la comparación del cliente); tabla del mando ATEC = Soyea; logos del ATEC (4:2:0) y del LG (4:2:2) girados = JPEG de Pillow; ATEC girado/propio: solo cambia el recurso 0x0300 y su CRC-16 queda correcto; LG girado: solo cambia la zona 0x370000–0x3B0000; logo propio demasiado grande → se recomprime hasta caber; ATEC con cliente `12 34` y tabla editada (inmediato `c0e68240`, 1 byte de tabla); ATEC rechaza `00 BF`; Konka con cliente `37 78` (en memoria `78 37`) y tabla editada (lo de detrás no cambia); LG con cliente `12 34` sin mando Soyea; elegir el mismo cliente que la opción da la v5 documentada |
| `ZopfliTests` | 7 vectores de py-zopfli idénticos; símbolos de distancia; `ExactLog` = glibc en 1..2 100 000; constantes de ln 2 |
| `ZlibTests` | 9 vectores de zlib 1.3 (Python) idénticos, incluida entrada vacía, aleatoria y > 1 MB |
| `ProfileTests` | Perfiles integrados válidos y coherentes con el catálogo; 22 perfiles manipulados rechazados; carpeta externa |
| `JpegEncoderTests` | 24 JPEG idénticos a Pillow 12.3.0 (4:4:4, 4:2:2 y 4:2:0; tamaños impares 37x29, 17x9, 2167x40 y 64x48; con y sin reinicios); submuestreos no admitidos |
| `LogoTests` | Codificador → JPEG válido; girar conserva formato (tablas, submuestreo, reinicios) en 4:4:4/4:2:2/4:2:0; bajar la calidad hasta caber; rechazo si no cabe ni al mínimo; JPEG exacto usado tal cual; otro submuestreo convertido; encajar sin deformar con bandas negras; PNG (RGB, RGBA) y BMP; PNG dañado; 3 000 JPEG y 2 000 PNG/BMP corruptos sin excepciones inesperadas; CRC-16 (`"123456789"` → `0xFEE8`); 12 reglas de `logo` en perfiles (TEL, CRC, zona de flash) |
| `KeyTableTests` | Tabla por defecto = v5 (codificada igual que el parche); exportar/importar; 4 tablas inválidas; códigos de otro mando admitidos; archivo de otro perfil o mal formado; perfil con tabla incoherente; tabla con la opción que no la aplica; formatos lista8/lista4/directa256 (ida y vuelta, terminadores); código de cliente (lectura, formatos invertido y `bg.sfeqi`, límite 7F); 9 vectores de `bg.sfeqi` de `aeonasm.py`; 6 `clienteMando` inválidos; los 3 perfiles con mando editable y tablas en su sitio idénticas al original |
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

Huellas de los logos girados (Pillow 12.3.0; `j` = JPEG original sacado del `.BIN`; `dri` = intervalo de reinicio
del JPEG principal, leyendo sus segmentos hasta SOS, no la miniatura EXIF):

```python
im = Image.open(io.BytesIO(j)); q = im.quantization
im.convert("RGB").rotate(180).save(b, "JPEG", qtables=[q[0], q[1]], subsampling=S, optimize=False,
                                   progressive=False, restart_marker_blocks=dri)   # sin restart si dri = 0
```

Vectores del codificador (`JpegEncoderTests`): la misma imagen determinista en Python (`TestImage`: degradados +
generador congruencial `s = (s*1103515245 + 12345) & 0x7FFFFFFF`), `qtables=[ql, qc]` con
`ql[i] = i*3 % 40 + 2`, `qc[i] = i*5 % 60 + 3` (Pillow las toma en orden natural), `subsampling` 0/1/2 y
`restart_marker_blocks` 0/3.

Vectores de Zopfli (Python, `pip install zopfli==0.4.3`): ver el generador en el comentario de
`ZopfliTests.Gen`; huella del logaritmo: `math.log(i)` empaquetado `'<d'` para i = 1..2 100 000.

## Resultado actual

252 pruebas, 252 superadas (con firmwares, Linux). Nuevas en la 1.3.0: ATEC + Soyea = archivo probado `8e797d5d…`
(5 sitios del cliente, también en el código de arranque sin comprimir, iguales que en el Soyea); 4 resoluciones del
ATEC con sus tiempos en la entrada del panel; parche sintético en el código de arranque (CRC, app sin recomprimir);
original distinto rechazado; 3 zonas prohibidas (MS_IMG_INFO, TEL, cruce con la app). Antes: 243. Nuevas (2026-11-22): 8 vectores de `bn.ori` de `aeonasm.py`;
campos de reposo del ATEC; 3 perfiles con reposo incoherente rechazados y 1 coherente aceptado; ATEC + Soyea con
las 3 instrucciones idénticas a las del firmware Soyea. En GitHub Actions (Windows, sin firmwares) se omiten las
que necesitan los `.BIN`.

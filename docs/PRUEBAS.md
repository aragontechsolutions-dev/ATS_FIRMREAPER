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
| `RealFirmwareTests` | 7 firmwares reales: huella, fin de flash, CRC y tamaño de app iguales a Python; 1 bit cambiado → inválido; cambio fuera del CRC → "desconocido válido" |

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

## Resultado actual

52 pruebas, 52 superadas (con firmwares); sin firmwares: 43 superadas, 9 omitidas.

# ATEC 32L18D → panel LG.Philips LP141WX1 (1280x800, 6 bits)

Guía técnica de la modificación de firmware. Fecha: 2026-09-30.

## 1. Equipos

| Elemento | Dato |
|---|---|
| TV | ATEC 32" modelo 32L18D (DTMB + NTSC-M) |
| Placa | MStar, ID de placa `BD_MST061B_10AZU_14014`, SBOOT `KK_CUBA_SBOOT_20140718` |
| Memoria | SPI de 4 MB (4 194 304 bytes) |
| Firmware original | `Atec_32_KPC_Salva.BIN`, SHA-256 `19491133011eaca67db793dcfb068771e19a58dac42eff849ca30993fbad647b` |
| Panel nuevo | LP141WX1 (TL)(A2): 1280x800, 6 bits, LVDS 1 canal, VCC 3.3 V, CCFL |

## 2. Estructura del firmware (descubierta por análisis)

| Offset flash | Contenido |
|---|---|
| `0x000000–0x01FFFF` | SBOOT/MBOOT y PM (sin comprimir). Firma `MSIF0000S3 MBOT0206…` |
| `0x019A74` | Lista de **modos de entrada** VGA/PC (NO es el panel; no tocar) |
| `0x020000` | **Cabecera de la aplicación** (6 × u32 LE, ver abajo) |
| `0x021180` | Bloque de información (`78 56 AA 55`, `PANEL_NAME`, `DVBT`) |
| `0x022080` | Tabla `TEL` de recursos (offset u32 BE, tamaño u32 BE, 3 bytes de atributos) |
| `0x023080` | Código del stub de arranque |
| `0x023880` | **Aplicación comprimida con deflate crudo** + u32 LE tamaño + 2 bytes (`F8 71`) |
| `0x1E9288` | Primer recurso de la tabla TEL (límite que no se puede pisar) |
| `0x1EBCB0` | Recurso deflate (1.8 MB descomprimido) |
| `0x2B5290` | Recurso LZMA (fuentes/gráficos) |
| `0x2D06B6` | Recurso deflate pequeño |
| `0x390000–0x3FFFFF` | Base de datos de ajustes de usuario (bancos duplicados) |

### Cabecera en 0x020000

| Campo | Original | Significado |
|---|---|---|
| +0x00 | `0x00020080` | inicio de la app en la flash |
| +0x04 | `0x00240000` | destino en RAM |
| +0x08 | `0x00409203` | fin en RAM = destino + (fin_flash − inicio_flash) |
| +0x0C | `0x001E9283` | fin en la flash (= fin del deflate + 6 bytes de cola) |
| +0x10 | `0` | — |
| +0x14 | `0xDC243619` | **CRC-32 no reflejado**, polinomio `0x04C11DB7`, init `0`, sin xor final, sobre `flash[inicio:fin]` (verificado exacto) |

## 3. Tabla de paneles (aplicación descomprimida)

La tabla empieza en el offset `0x2A021C` de la app descomprimida y tiene 5 entradas de 92 bytes (`0x5C`):

| # | Nombre | Resolución | Canal |
|---|---|---|---|
| 0 | FullHD_CMO216_H1L01 | 1920x1080 | doble |
| **1** | **WXGA_AU20_T200XW02** | **1366x768** | **simple (la que usa este TV)** |
| 2 | SXGA_AU17_EN05 | 1280x1024 | doble |
| 3 | WXGAP_CMO_M190A1 | 1440x900 | doble |
| 4 | WSXGA_AU22_M201EW01 | 1680x1050 | doble |

### Campos de cada entrada (offset relativo al inicio)

| Off | Tipo | Campo MStar | Original (#1) | Nuevo |
|---|---|---|---|---|
| 0 | u32 | puntero al nombre | — | sin cambio |
| 4 | u8 | bits: b0 Dither, b1-4 LinkType, b5 DualPort, b6 SwapPort | `0x02` | 6 bits: `0x03` (dither on) / 8 bits: `0x02` |
| 5 | u8 | más bits (swaps, TI mode…) | `0x60` | sin cambio |
| 12–18 | u16×4 | OnTiming1/2, OffTiming1/2 | 30/400/80/30 | sin cambio |
| 20–23 | u8×4 | HSyncWidth, HSyncBackPorch, VSyncWidth, VBackPorch | 20/40/4/34 | **32/80/6/15** |
| 24,26 | u16 | HStart, VStart | 60/38 | **112/21** |
| 28,30 | u16 | Width, Height | 1366/768 | **1280/800** |
| 32–36 | u16×3 | Max/Typ/Min HTotal | 1722/1560/1414 | **1480/1440/1368** |
| 38–42 | u16×3 | Max/Typ/Min VTotal | 822/806/789 | **848/823/809** |
| 44–46 | u8×3 | Max/Typ/Min DCLK (MHz) | 88/76/60 | **75/71/66** |
| 48–54 | — | SSC, dimming PWM | 25/192, A0/FF/50 | sin cambio |
| 56 | u32 | aspecto | 1 | sin cambio |
| 64 | u32 | m_ucTiBitMode | 2 (8 bits) | 6 bits: **3** / 8 bits: 2 |
| 68 | u32 | m_ucOutputFormatBitMode | 0 | 6 bits: **1** / 8 bits: 0 |
| 76 | u32 | MaxSET | 2157070 | **1960972** |
| 80 | u32 | MinSET | 1470729 | **1725656** |
| 84 | u32 | OutTimingMode | 2 | sin cambio |

Comprobaciones de coherencia:
- Original: HStart = HSync + HBP (60 = 20 + 40; 38 = 4 + 34) y 1560 × 806 × 60 Hz = 75.4 MHz ≈ DCLK 76.
- Nuevo: 1440 × 823 × 60 Hz = 71.1 MHz ≈ DCLK 71 (hoja de datos: 71.0 MHz).
- SET = K / DCLK, con K = 129 424 176 deducida de la entrada original (MaxSET·MinDCLK = MinSET·MaxDCLK).

## 4. Lo verificado y lo que no

**Verificado en el binario:**
- La cabecera, el rango y el algoritmo del CRC-32 (reproduce `0xDC243619` exacto).
- La compresión deflate y el tamaño de la cola.
- La ubicación y los campos de geometría, porches, totales, DCLK y un canal (cuadran matemáticamente).
- Que las salidas solo cambian la entrada del panel dentro de la app. Fuera de `0x20000–0x1E9288` los archivos son idénticos al original.

**Inferido (NO verificado en hardware):**
- El valor de los enums de bits: TiBitMode 3 = 6 bits, OutputFormatBitMode 1 = 6 bits. Se basa en el SDK MStar: registro MOD_94, "01 = 6 bit, 10 = 8 bit"; y en que los paneles de 8 bits de la tabla usan TiBitMode = 2.
- Los 2 bytes `F8 71` tras el tamaño: se desconoce su función. Se conservan tal cual. Si fueran un checksum de la app descomprimida, el TV no arrancaría y habría que reflashear el original.
- Que no exista otra copia de los parámetros del panel en MBOOT. No se encontró ninguna, ni en claro ni en los recursos.

## 5. Archivos

| Archivo | Uso |
|---|---|
| `patch_panel.py` | Script reproducible: extrae, parchea, recomprime, recalcula el CRC y se verifica solo |
| `Atec32L18D_LP141WX1_6bit.BIN` | **Probar primero.** Salida LVDS a 6 bits + dither |
| `Atec32L18D_LP141WX1_8bit.BIN` | Respaldo: misma resolución, salida a 8 bits (necesita mapeo JEIDA; el par 3 queda sin conectar) |

Regenerar: `python3 patch_panel.py Atec_32_KPC_Salva.BIN`

## 6. Procedimiento de prueba (por etapas)

1. **Respaldo:** lee la SPI de la placa 3 veces y compara los hashes. Guarda el original en 2 sitios.
2. **Grabar** `Atec32L18D_LP141WX1_6bit.BIN` con el programador: borrar, escribir y **verificar**. Ojo con el CH341A negro: saca 5 V en los datos si no tiene la modificación a 3.3 V.
3. **Primer arranque sin panel:** comprueba que la placa arranca (LED de standby, respuesta al mando) y mide 3.3 V en el VCC del conector del panel.
4. **Con panel y sin inversor:** alumbra con una linterna para ver si hay imagen.
5. **Con inversor.** Entra en el menú de fábrica y haz *Restore/Reset factory*, por si la base de datos guarda valores del panel anterior.
6. **Si los colores están mal:** prueba el ajuste TI MODE del menú de servicio o graba la variante de 8 bits.
7. **Si no arranca:** reflashea `Atec_32_KPC_Salva.BIN` y avisa. Lo más probable es que los 2 bytes `F8 71` sean un checksum que hay que calcular.

## 7. Seguridad

- Panel: VCC máximo absoluto 4.0 V. El regulador de 3.3 V va después del MOSFET de encendido del panel.
- Límites del PLL (MinSET/MaxSET) ajustados al rango del panel (66–75 MHz), para no mandarle un reloj fuera de especificación.
- Inversor CCFL: unos 675 Vrms. Aislarlo y no tocar con el equipo encendido.
- El script se niega a parchear si la entrada no es exactamente la esperada, si la app no cabe o si algún CRC falla.

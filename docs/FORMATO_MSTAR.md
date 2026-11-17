# Formato del firmware MStar (SPI 4 MB, CPU AEON R2)

Guía técnica de lo que el motor lee. **V** = verificado en los binarios del proyecto;
**SDK** = coincide con el SDK Chakra3 filtrado (ver `reports/`); **?** = no verificado.

## Mapa de la flash

| Offset | Contenido | |
|---|---|---|
| `0x000000–0x00FFFF` | SBOOT (cargador) | SDK |
| `0x010000–0x01FFFF` | Código PM (reposo, mando en standby) | SDK |
| `0x020000` | Cabecera de la app (6 × u32 LE) | V |
| `0x020080` | Inicio de la app; primeros `0x3800` bytes sin comprimir (stub) | V/SDK |
| `0x021180` | MS_IMG_INFO (magic `78 56 AA 55`) | V |
| `0x022080` | Tabla TEL de recursos | V |
| `0x023880` | Deflate crudo de la app (RFC 1951) + cola de 6 bytes | V |
| fin app … primer recurso | Zona libre (ceros) | V |
| primer recurso TEL … | Recursos (logo JPEG, fuentes LZMA, etc.), cada uno con CRC-16 al final | V |
| fin del área TEL (`0x22088`) … final | Zona escribible por el TV: logo capturado (LG), bases de datos (ajustes, canales, fábrica, HDCP…) | V/SDK |

## Cabecera en `0x20000`

| Campo | Significado | |
|---|---|---|
| +0x00 | inicio de la app en flash = `0x20080` | V |
| +0x04 | destino en RAM (`0x240000` en ATEC/LG/Soyea; `0x200000` en Konka) | V |
| +0x08 | fin en RAM = destino + (fin flash − inicio flash) | V |
| +0x0C | fin en flash = fin del deflate + 6 | V |
| +0x10 | 0 | V |
| +0x14 | CRC-32 MSB de `flash[0x20080 : fin_flash]` | V |

**CRC:** polinomio `0x04C11DB7`, sin reflejar, valor inicial 0, sin XOR final
(`crcmod.mkCrcFun(0x104C11DB7, 0, 0)`). Vector de prueba: `CRC("123456789") = 0x89A1897F`.
Cubre también MS_IMG_INFO y la tabla TEL, así que si se tocan hay que recalcularlo.

## Cola tras el deflate (6 bytes)

`u32 LE tamaño descomprimido` + 2 bytes de función **desconocida** (?). Se conservan siempre.
Probado: no son CRC16 0x8005/0x1021 (init 0/FFFF) del comprimido, de la app ni de `0x20080..` (el CRC-16 de los
recursos TEL, en cambio, sí es 0x8005: ver más abajo).

## MS_IMG_INFO (`0x21180`)

Magic `78 56 AA 55`, textos `DVBT-----`, `PANEL_NAME`, y en `0x211C7` la marca de compresión
`BE AD DE` (deflate; `EF AD DE` = LZMA según el SDK) seguida de u32 LE = **longitud de la app en flash**
(V en los 3 binarios). Los scripts no la actualizan y los firmwares funcionaron → no la comprueba el arranque.

## Tabla TEL (`0x22080`)

```
"TEL\x09" "X3i\0"  u32 BE fin del área de recursos  u16 BE nº entradas
entrada (11 bytes): u16 BE id, u32 BE offset, u32 BE longitud, u8 banderas
terminador: id 0xF000 (resto CC)
```
Banderas: `0x00` sin comprimir, `0x01` LZSS, `0x04` deflate, `0x08` LZMA. El **primer recurso** marca el límite
que la app no puede pasar.

**Fin del área de recursos** (u32 en `0x22088`, antes llamado "tamaño reservado"): `0x3C0000` en ATEC, Soyea y
Konka; `0x370000` en el LG. En el LG coincide con `s_Flash_u32WriteValidAddr_Start` de `msAPI_Flash_Init`
(app `0x26FF53`): desde ahí la flash la puede escribir el propio TV. (V)

**CRC-16 de cada recurso** (V en los 41 recursos de los 4 originales): los 2 últimos bytes (big-endian) son el
CRC-16 de todo lo anterior del recurso; polinomio `0x8005`, valor inicial 0, sin reflejar, sin XOR final
(CRC-16/UMTS; `CRC("123456789") = 0xFEE8`). Los recursos miden múltiplos de 8: datos + ceros + CRC.
La búsqueda de recursos del ATEC (`0x322A4A`) **no** lo comprueba, y el `TV4-32K16_MIRROR_MENU.bin` (logo girado
por el script Python, CRC sin actualizar) funciona en el TV. FirmReaper lo recalcula igualmente cuando cambia un
recurso con `crc16` en el perfil, y el análisis avisa si no coincide.

### Logo de arranque (V)

| Firmware | De dónde lo carga el programa del TV | JPEG original |
|---|---|---|
| ATEC | Siempre el recurso TEL `0x0300` (función `0x2E350F`; copia longitud+7 & ~7 bytes a RAM `0x1F30000`) | 2167x1228, 4:2:0, reinicio 136 |
| Soyea | Recurso TEL `0x0300` | 720x480, 4:4:4 |
| Konka | Recurso TEL `0x065E` (función `0x26EC94`) | 1366x768, 4:4:4, reinicio 171 |
| LG | Función `0x2C238C`: si el ajuste de logo (RAM `0x53F565`) vale 2, lee **0x40000 bytes desde `0x370000`** ("logo capturado": "LG Life's Good"); si no, el recurso TEL `0x0301` (imagen **azul lisa**) | `0x370000`: 1376x768, 4:2:2, sin reinicios, resto de la zona a `FF` hasta `0x3B0000` |

En el LG el desplazamiento dentro de la zona sale de `(ajuste << 14) & 0xC0000`; con el valor del volcado del
usuario es `0x370000` (en `0x3B0000` empiezan otros datos, no un JPEG). Confirmado por el usuario: su TV muestra
"LG Life's Good" al encender.

| Binario | Recursos | Primer recurso | Fin app | Libre |
|---|---|---|---|---|
| ATEC original | 11 | `0x1E9288` | `0x1E9283` | 5 B |
| LG original | 10 | `0x1850D0` | `0x1850CB` | 5 B |
| LG v5 (zopfli) | 10 | `0x1850D0` | `0x17D6B3` | 31 261 B |
| Soyea | 10 | `0x1EAA80` | `0x1EAA7F` | 1 B |

## IDs de placa y SBOOT

| Binario | ID de placa (`BD_…`) | SBOOT |
|---|---|---|
| ATEC 32L18D | `BD_MST061B_10AZU_14014` | `KK_CUBA_SBOOT_20140718` |
| Soyea | `BD_MST061B_10AZU_14014` | `KK_CUBA_SBOOT_20140718` |
| LG 42LF652VX | `BD_VST56_PB801` | — |

## Mando (código de cliente y tabla) (V)

Detalle y método en `docs/historial/README_MANDO_GENERICO.md`. Código de cliente escrito en el orden de envío
(Soyea = `37 78`).

| Firmware | Cliente | Tabla código → función |
|---|---|---|
| LG | `0x39DEFE`, en memoria `b2 b1` (original `00 BF`; mando 2 de fábrica `88 90` en `0x39DEFC`) | `0x39DD94`, 45 × 8 bytes: u32 `0x0100\|código`, función |
| ATEC / Soyea | `bg.sfeqi r7,Y` en `0x3233D5` / `0x3251E3`, `Y = b2·256 + b1` con signo (≤ 0x7FFF); también acepta `A0 5F` | `0x422090` / `0x423CE0`, 256 bytes: byte[código] = función |
| Konka | `0x38FFA2`, en memoria `b2 b1` (original `07 07`); mando de fábrica `0x8890` en el código | `0x38FFA4`, 46 × 4 bytes: código, función, 0, 0 (fin: función `FF`); se copia a RAM `0x3EA7D4` al arrancar |

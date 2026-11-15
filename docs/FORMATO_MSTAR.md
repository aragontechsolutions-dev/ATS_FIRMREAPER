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
| primer recurso TEL … | Recursos (logo JPEG, fuentes LZMA, etc.) | V |
| final de la flash | Bases de datos (ajustes, canales, fábrica, HDCP…) | SDK |

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
Probado: no son CRC16 0x8005/0x1021 (init 0/FFFF) del comprimido, de la app ni de `0x20080..`.

## MS_IMG_INFO (`0x21180`)

Magic `78 56 AA 55`, textos `DVBT-----`, `PANEL_NAME`, y en `0x211C7` la marca de compresión
`BE AD DE` (deflate; `EF AD DE` = LZMA según el SDK) seguida de u32 LE = **longitud de la app en flash**
(V en los 3 binarios). Los scripts no la actualizan y los firmwares funcionaron → no la comprueba el arranque.

## Tabla TEL (`0x22080`)

```
"TEL\x09" "X3i\0"  u32 BE tamaño reservado  u16 BE nº entradas
entrada (11 bytes): u16 BE id, u32 BE offset, u32 BE longitud, u8 banderas
terminador: id 0xF000 (resto CC)
```
Banderas: `0x00` sin comprimir, `0x01` LZSS, `0x04` deflate, `0x08` LZMA. Logo de arranque: id `0x0300`
(ATEC/Soyea) / `0x0301` (LG), JPEG. El **primer recurso** marca el límite que la app no puede pasar.

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

# RCA (BD_MST030B_10AL8_12052) — mando Soyea: análisis y PRUEBA v1 (2026-11-22)

Original: `RCA_letra_roja_fondo_blanco_con_perros.BIN`, SHA-256 `669bdb5e3a8ad4bbe42639546671c560c47add9577d8a63d2f3f8397135aa307`.
Contenedor MStar válido; app en RAM `0x200000` (base `0x203800`), misma familia que el Konka (mismo controlador IR,
textos "IR:%02dh", "<Ir Resend…>"). Direcciones = RAM de la app.

## Mando (comprobado en el código)

| Qué | Dónde | Original |
|---|---|---|
| Código de cliente (en memoria `b2 b1`, como el Konka) | `0x398AB0` | `07 07` |
| Reposo (registros IR `0x3DA4`/`0x3DA5`) | `0x2A798A` / `0x2A7BC5` los calculan **desde `0x398AB0`** | — |
| Lista código→tecla `[código, tecla, 0, 0]`, fin en tecla `FF` | `0x398AB4`; cargador `0x262AEF` (copia a RAM `0x3EBEE4`) | 39 teclas |
| Detrás de la lista | `0x398B54`, `0x398B58`, `0x398B5C`, `0x398B60`: otras listas (las usa `0x262B5A`…) | **no se tocan** → máximo 39 teclas |

- Numeración comprobada (rutina `0x262F9B`, igual que la del Konka): flechas `0x0D`–`0x10`, VOL± `0x12`/`0x13`,
  CH± `0x14`/`0x15`. Dígitos `0x02`–`0x0B` (códigos `04 05 06 08 09 0A 0C 0D 0E 11` = 1…9, 0), POWER `0x01`, OK `0x11`.
- El mando original del RCA **no** usa `0x0D`/`0x0E` ni `0x14`/`0x15`: sus botones de código `0x60`/`0x61` (arriba/abajo
  en el Konka) dan `0x19`/`0x18`.
- El resto de valores (`1A 1B 1C 1E 1F 20 23 28 29 2A 2B 2C 40 43 52 53 54 55 57 5E 63`) **no tienen nombre
  comprobado**. La tabla de teclas rápidas con nombres (`0x2A3619`, "hot key mute", `VK_…`) es la del reproductor
  multimedia, con otra numeración: no sirve para esto.
- Compresión: el original no se reproduce con zlib (sale más grande que el hueco: solo 2 bytes libres). Zopfli, 5
  iteraciones: 856 602 bytes → 17 488 bytes libres.

## Prueba v1

Perfil de prueba: `docs/historial/pruebas/rca-prueba-mando-soyea.json` (copiar a la carpeta `perfiles` junto al
programa para regenerarlo). Archivo: `rca-prueba-mando-soyea.BIN`, SHA-256
`756faf2351d3e43095e5c82b4876e78edbc538b43693f2af017dde5c5a99369a`. Cambian 75 bytes de la app (cliente + lista).

Criterio: las **39 funciones del mando original del RCA**, cada una en un botón del Soyea, sin valores nuevos. Las
comprobadas van a su botón; las demás, al botón más probable ("supuesto"). Botones del Soyea sin función: DASH (`0A`), FAV (`20`), TV/RADIO (`25`), MTS (`40`), CH.LIST (`49`), TEMP_DET (`4F`), EPG (`51`), `56`, `57`.

| Código Soyea | Botón Soyea | Valor RCA | Por qué | Qué hace en el TV (rellenar) |
|---|---|---|---|---|
| `00` | 0 | `0x02` | comprobado | |
| `01` | 1 | `0x03` | comprobado | |
| `02` | 2 | `0x04` | comprobado | |
| `03` | 3 | `0x05` | comprobado | |
| `04` | 4 | `0x06` | comprobado | |
| `05` | 5 | `0x07` | comprobado | |
| `06` | 6 | `0x08` | comprobado | |
| `07` | 7 | `0x09` | comprobado | |
| `08` | 8 | `0x0A` | comprobado | |
| `09` | 9 | `0x0B` | comprobado | |
| `0B` | RETURN | `0x20` | supuesto | |
| `0C` | POWER | `0x01` | comprobado | |
| `0D` | SOURCE | `0x1F` | supuesto | |
| `0E` | MUTE | `0x1E` | supuesto | |
| `0F` | MENU | `0x1C` | supuesto | |
| `14` | OK | `0x11` | comprobado | |
| `15` | VOL+ | `0x12` | comprobado | |
| `16` | VOL- | `0x13` | comprobado | |
| `17` | CH+ | `0x19` | = botón "arriba" del mando RCA (código 0x60) | |
| `18` | CH- | `0x18` | = botón "abajo" del mando RCA (código 0x61) | |
| `19` | PICTURE | `0x28` | supuesto | |
| `1A` | AUDIO | `0x23` | supuesto | |
| `1C` | INFO | `0x1B` | supuesto | |
| `1D` | EXIT | `0x1A` | supuesto | |
| `21` | ROJO | `0x29` | supuesto | |
| `22` | VERDE | `0x2A` | supuesto | |
| `23` | AMARILLO | `0x2B` | supuesto | |
| `24` | AZUL | `0x2C` | supuesto | |
| `28` | PAGE UP | `0x0F` | IZQUIERDA (comprobado) | |
| `29` | PAGE DOWN | `0x10` | DERECHA (comprobado) | |
| `45` | SLEEP | `0x5E` | supuesto | |
| `46` | CC | `0x43` | supuesto | |
| `47` | SUBTITLE | `0x63` | supuesto | |
| `4B` | PLAY | `0x52` | supuesto | |
| `4C` | STOP | `0x53` | supuesto | |
| `4E` | PREV | `0x40` | supuesto | |
| `53` | NEXT | `0x57` | supuesto | |
| `54` | REW | `0x54` | supuesto | |
| `55` | FF | `0x55` | supuesto | |

**Pruebas extra:** (1) apagar con el mando y **encender desde reposo con el Soyea**; (2) desenchufar, enchufar y
encender con el Soyea; (3) en el menú: ¿VOL+/VOL− mueven a derecha/izquierda? ¿CH+/CH− mueven arriba/abajo?

Con los resultados se hará la v2 (definitiva) y, si funciona, el perfil integrado del RCA.

# LG 42LF652VX (TP.V56.PB801) — usar el mando del SOYEA LKK32CMB

Guía técnica, versión 3. Fecha: 2026-11-14.

- **v1:** las teclas salían cambiadas.
- **v2:** las teclas funcionan, pero en el reproductor USB la cruceta seguía cambiando volumen y canal (probado por el usuario).
- **v3:** en el reproductor USB la cruceta solo se desplaza, pero así no se puede cambiar el volumen mientras se reproduce (probado por el usuario).
- **v4:** durante la reproducción USB a pantalla completa devolvía VOL y CH. No era lo pedido.
- **v5:** igual que la v3, pero durante la reproducción USB a pantalla completa los botones de volumen vuelven a cambiar el volumen. CH+/CH− siguen siendo ARRIBA/ABAJO.

## 1. Archivos

| Archivo | Descripción |
|---|---|
| `LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN` | Mando Soyea + imagen **sin espejo**. SHA-256 `3a836aee…36fed599` |
| `LG_42LF652VX_MANDO_SOYEA_v5.BIN` | Mando Soyea + espejo igual que el original (activado). SHA-256 `17918f8f…5b9fd9b002` |
| `patch_ir_lg.py` | Genera los dos desde el original y los verifica (`pip install zopfli`; añade `--espejo-off` para la segunda variante) |
| `tools/build_cave_v2.py` | Ensambla la rutina nueva y la verifica desensamblándola |
| Original LG | `MST_EXT_SPI_ISP_20220511_165639.BIN`, SHA-256 `ef5c8055…36bee871` |

- Las versiones anteriores se han borrado; la v5 las sustituye.
- Usa la variante de espejo que te haya dado la imagen correcta con la v2.

## 2. Por qué fallaba la v1: cómo relaciona el LG código y botón

El mando manda (cliente, código). El LG convierte el código en una función en **tres pasos**:

```
FIFO IR 0x540664 → 0x2F6A45: compara el cliente
      00 BF (0x39DEFE) → mando 1  |  88 90 (0x39DEFC) → mando 2      (0x517B61 = 1 / 2)
→ 0x2B49F5: estructura de tecla 0x51DD70 { +0 = banderas (bits 3-4 = mando), +1 = código }
→ 0x303D2E: busca (0x0100 | código) en la TABLA FLASH 0x39DD94 (45 entradas × 8 bytes:
            u32 código, u8 índice)   →  +4 = índice
→ switch 0x2B4D48 (tabla de saltos 0x393824) indexado por el ÍNDICE → KEY_xxx (0x5179A4)
```

- **Error de la v1:** tradujo el código Soyea al *índice* y lo metió como si fuera un *código*. El LG lo volvía a pasar por la tabla, así que todo quedaba desplazado. Por ejemplo, el "0" del Soyea daba índice 2, y la tabla convertía el código 0x02 en el índice 3, que es el "1".
- **El Soyea**, en cambio, hace un solo paso: código → tabla `0x423CE0` → índice interno → su switch (`0x2C5AF4`) → KEY. Se comprobó que el índice de su switch (`0x5DA88D`) es directamente el valor de esa tabla (`0x32519F` → `0x26515A` → `0x2C6276`).
- Los dos firmwares usan el mismo enumerado `KEY_xxx` de MStar Chakra, así que se puede emparejar cada botón por la KEY que produce.

### La cruceta del Soyea

El mando Soyea **no tiene flechas separadas**:
- La cruceta envía VOL+ (15), VOL− (16), CH+ (17) y CH− (18).
- Su firmware las convierte en DERECHA, IZQUIERDA, ARRIBA y ABAJO cuando hay un menú abierto (`0x2C58F8`).

El mando LG original sí tiene flechas propias, así que el LG nunca hace esa conversión para el mando. **Sí la hace para su teclado frontal** (`0x303BF0`), que tiene el mismo problema. La v2 aplica al mando Soyea **ese mismo criterio del propio LG**.

## 3. Cambios (203 bytes en la aplicación; 204 con espejo OFF)

| Dirección | Cambio |
|---|---|
| `0x39DEFE` | cliente del mando 1: `00 BF` → `37 78` (igual que en la v1; ya se vio que el TV recibe el Soyea) |
| `0x39DD94` | tabla código → índice del mando 1 reescrita con los 44 códigos Soyea (360 bytes, mismo formato, terminador `FFFF`) |
| `0x2B4A60` | `jal 0x303D2E` → `jal 0x3BB400` |
| `0x3BB400` | rutina de 121 bytes + 4 de tabla (textos ASF-MJPEG sin referencias) |
| `0x25E644` | **solo `ESPEJO_OFF`**: `sb 0x163(r1),r5` → `sb 0x163(r1),r0` (bMirror de XC_INITDATA = 0) |

- Se elimina el gancho de la v1 en `0x2B4A0E`, y su tabla y rutina: esas direcciones vuelven a tener los bytes originales.

**Rutina (`0x3BB400`):**

```
llama a 0x303D2E (original: código → índice)
si la tecla es del mando 1 (banderas & 0x18 == 8) y el índice está entre 0x12 y 0x15 (VOL+/−, CH+/−):
    OSD 0x10 (reproductor USB):
        si no se reproduce nada (0x278DC1() == 0)        → flechas
        si el explorador de archivos (ventana 0x129) es visible → flechas
        si no (reproducción a pantalla completa)          → índices 12/13 sin cambiar (volumen); 14/15 → ARRIBA/ABAJO (v5)
    si OSD activo == 7, o 0x303BA8() (pantalla navegable, el mismo test que usa el teclado del TV):
        índice = {0x12→0x10 DERECHA, 0x13→0x0F IZQUIERDA, 0x14→0x0D ARRIBA, 0x15→0x0E ABAJO}
retorna
```

- Usa r6, r7 y r3, que no se conservan entre llamadas, y guarda r9 y r10 en la pila.
- `0x303EDF`, la siguiente llamada del original, no usa r3.

**Comprobado que no se rompe nada:**
- Los demás lectores de la tabla buscan por **índice**, no por código:
  - la inicialización `0x303941` (tabla RAM `0x540784` y sus ajustes `0x45ECB8`–`0x45ECE0`);
  - `0x303B01`;
  - `0x303B7A`, que pregunta si existen los índices 0x29 y 0x17.
- Se conservan todos los índices que tenían botón equivalente, y no hay índices repetidos.

### v3: cómo se identificó la pantalla del reproductor USB (OSD 0x10)

- **La tecla USB:** `KEY_DMP` (0x6E), en `0x2B5D1F` → `0x2B5F18`, cambia la fuente a 0x15 (DMP).
- **El estado 13** de la máquina principal (`0x2BA109`, tabla `0x394130`) ejecuta `0x2C0B97`, que abre el OSD **0x10** con `0x2D75F9`.
- **La función de arranque** del OSD 0x10 (tabla `0x396AC4` → `0x2EAF62`) está en la misma zona que el código de gestión de archivos USB (`0x2ED00E`, "EN_EXE_DMP_FILE_MANAGE_DEL").

### v4/v5: navegar o reproducir dentro del USB

Se usa el mismo criterio que el propio LG aplica a su teclado frontal en el USB (`0x303C99`):
- `0x278DC1` devuelve 1 si hay reproducción en curso. Lee las banderas `0x51C408 | [0x51C408+0x58]` & `0x3C0` (`cmovri` = F ? rA : imm, comprobado en la especificación oficial).
- `0x2C5152(0x129)` dice si el explorador de archivos es visible.
  - La ventana 0x129 es la rejilla de 7 columnas que mueve `0x2ED00E`.
  - Cuando está oculta y se pulsa una flecha, el firmware la vuelve a mostrar (`0x2EFFC8`).
- La diferencia con el teclado del LG: este solo da volumen con vídeo (`0x2C076E() & 4`). Aquí se da con cualquier reproducción a pantalla completa.

**Limitación física (deducida, sin confirmar en el TV):**
- En el Soyea, el botón VOL+ y la flecha derecha mandan el mismo código (`15`), y VOL− y la flecha izquierda también (`16`).
- Indicios:
  - La tabla del Soyea (`0x423CE0`) solo da volumen a los códigos 15/16, y su firmware decide entre VOL y flecha según la pantalla.
  - En la tabla del LG ningún otro código lleva al índice de volumen.
- Por eso el TV no puede distinguir qué botón se pulsó. En la reproducción a pantalla completa, derecha/izquierda de la cruceta también cambian el volumen.
- Si al probar resulta que los botones de volumen mandan otro código, se puede separar del todo.

**Efecto (v5):**
- En los menús del USB y en el explorador, todo son flechas.
- Reproduciendo a pantalla completa: VOL± es volumen; ARRIBA/ABAJO siguen siendo flechas.
- Con la lista visible durante la reproducción, todo son flechas.

## 4. Tabla de botones (simulación del firmware final)

| Código Soyea | Botón | Índice LG | KEY en el LG |
|---|---|---|---|
| 00–09 | 0–9 | 02–0B | KEY_0–KEY_9 |
| 0A | -/-- | 0C | DASH |
| 0B | RETURN | 1D | CHANNEL_RETURN |
| 0C | POWER | 01 | POWER |
| 0D | SOURCE | 22 | INPUT_SOURCE (el mismo índice que el botón SOURCE del mando LG) |
| 0E | MUTE | 21 | MUTE |
| 0F | MENU | 1C | MENU |
| 14 | OK | 11 | SELECT |
| 15 | VOL+ / ► | 12 | VOLUME_PLUS; **en menús y navegando el USB, RIGHT** (en la reproducción USB a pantalla completa, volumen) |
| 16 | VOL− / ◄ | 13 | VOLUME_MINUS; **en menús y navegando el USB, LEFT** (en la reproducción USB a pantalla completa, volumen) |
| 17 | CH+ / ▲ | 14 | CHANNEL_PLUS; **en menús y en todo el USB, UP** |
| 18 | CH− / ▼ | 15 | CHANNEL_MINUS; **en menús y en todo el USB, DOWN** |
| 19 | PICTURE | 24 | PICTURE |
| 1A | AUDIO | 1F | AUDIO |
| 1C | INFO | 1E | INFO |
| 1D | EXIT | 1A | EXIT |
| 20 | FAV | 17 | CHANNEL_FAV_LIST |
| 21–24 | ROJO, VERDE, AMARILLO, AZUL | 29–2C | RED, GREEN, YELLOW, BLUE |
| 28 / 29 | PAGE UP / DOWN | 4C / 4D | PAGE_UP / PAGE_DOWN |
| 45 | SLEEP | 26 | SLEEP |
| 46 | CC | 43 | CC |
| 47 | SUBTITLE | 49 | SUBTITLE |
| 49 | CH.LIST | 16 | CHANNEL_LIST |
| 4B / 4C | PLAY / STOP | 51 / 53 | PLAY / STOP |
| 4E / 53 | PREV / NEXT | 56 / 57 | PREVIOUS / NEXT |
| 54 / 55 | REW / FF | 54 / 55 | REWIND / FF |
| 56 / 57 | (fabricante) | 19 / 18 | KEY 0x82 / 0x81 |

**Sin función** (el LG no tiene esa KEY): Soyea `25` TV/RADIO, `40` MTS, `4F` TEMP_DET y `51` EPG.

## 5. Imagen invertida (espejo)

- El firmware LG **original** ya arranca con espejo: en `0x25E5D7` rellena XC_INITDATA con `bMirror` (+0x1B, `0x163(r1)`) = r5 = 1 fijo (`0x25E60D movi r5,1`). La v1 no tocó eso.
- Es decir, este archivo está compilado para un panel montado al revés. Si tu panel va al derecho, la imagen sale invertida con este firmware, lleve o no el parche del mando.
- `ESPEJO_OFF` pone `bMirror = 0` (vídeo).
- El menú de fábrica de este firmware tiene un elemento **"Mirror"** (junto a "Preset Panel ID", "LVDS MAP", "PWM REF"…). No he localizado su variable, así que no sé con seguridad qué controla. Puede que permita corregirlo también sin cambiar el firmware.
- **No verificado:** si los menús (OSD) también salen al revés con `ESPEJO_OFF`. Las funciones del espejo del OSD (`0x2AFC1C`/`0x2AFC5A` → `MApi_GOP_GWIN_SetH/VMirror`) dependen de un estado (`0x53ED78`) que no he identificado. Si pasa, avísame y lo corrijo.

## 6. Prueba

1. Lee y guarda el original (3 lecturas con el mismo hash).
2. Graba la v5 con la misma variante de espejo que usaste antes: borrar → escribir → verificar.
3. Comprueba que la imagen sale igual que con la versión anterior.
4. Viendo TV: los números, VOL± y CH± (cruceta), MUTE, SOURCE, INFO, EXIT, RETURN, FAV y CH.LIST.
5. Dentro de MENU: la cruceta debe mover el cursor y OK debe confirmar. Prueba también la lista de fuentes y la de canales.
6. USB:
   - Comprueba que la cruceta **solo se desplaza** por carpetas y archivos, y que no cambia el volumen ni el canal.
   - Prueba PLAY, STOP, PREV/NEXT y REW/FF.
   - Reproduce un vídeo a pantalla completa: VOL+ y VOL− deben cambiar el volumen, y ARRIBA/ABAJO no deben cambiar de canal.
   - Haz lo mismo con música y fotos.
   - En la lista de archivos, pulsa VOL+: si el cursor se mueve a la derecha, se confirma que comparte código con la flecha derecha.
   - Con la lista visible durante la reproducción, la cruceta mueve el cursor.
   - Si alguna otra pantalla se comporta mal, apunta cuál es.
7. Encender desde reposo con POWER: pendiente (etapa 2). El código de reposo (PM, `0x10000`–`0x1A000`) espera el mando genérico MStar `80 7F` y no se ha modificado.

# Mando genérico (código de cliente + tabla) en LG, ATEC y Konka — análisis (2026-11-17)

Objetivo: que en los 3 firmwares conocidos se pueda elegir **a qué mando obedece el TV** (código de cliente NEC) y
**qué hace cada botón** (tabla código → función). Direcciones = RAM de la app. Desensamblado con `tools/aeon`.

## 1. Notación del código de cliente

Un mando NEC envía 4 bytes: cliente 1, cliente 2, comando, ~comando. FirmReaper escribe el cliente como los dos bytes
**en el orden en que se envían**: el Soyea LKK32CMB es `37 78`. Comprobación:

- **ATEC** (`0x3233AA`): toma la trama de 32 bits; cliente = 16 bits bajos (`exthz`), comando = byte 2
  (`srli 0x10`), su complemento = byte 3 (la suma debe ser `0xFF`). Los bytes van por orden de llegada de menos a más
  significativo → cliente = `b2·256 + b1`. El Soyea (mismo firmware) compara con `0x7837` → `b1 = 37`, `b2 = 78`.
- **LG**: el Soyea `37 78` (probado en el TV) está en memoria como `78 37` (`0x39DEFE`) → orden invertido.
- **Konka** (`0x2A60DF`, decodificador por software): tras 8 bits hace `r23 = byte << 8` y tras 16 `r23 |= byte` →
  el 1.er byte queda en la parte alta; el u16 se guarda en memoria (little-endian) como `b2 b1` → orden invertido.
- Coincidencia que lo confirma: el mando de fábrica `88 90` aparece como "mando 2" en el LG y como `0x8890` en el Konka.

| TV | Dónde | Formato en el perfil | Original |
|---|---|---|---|
| LG | `0x39DEFE` (dato, mando 1) | `bytes-invertidos` | `00 BF` |
| ATEC | `0x3233D5` `bg.sfeqi r7,Y` | `aeon-sfeqi` (Y = b2·256 + b1) | `36 77` |
| Konka | `0x38FFA2` (dato) | `bytes-invertidos` | `07 07` |

**Límite del ATEC:** `Y` es un inmediato de 16 bits **con signo** (`aeon_isa.json`, letra Y, flags 2) y el cliente
recibido se carga sin signo (`exthz`), así que solo funcionan códigos con `b2 ≤ 7F` (por ejemplo, el del LG `00 BF`
no). Ampliarlo necesitaría cambiar más código: no se hace. El segundo cliente aceptado por el ATEC (`A0 5F`) y los
mandos de fábrica `88 90` del LG y del Konka no se tocan.

Codificación de `bg.sfeqi rA,Y` (AEON R2): `110 000 AAAAA YYYYYYYYYYYYYYYY 0 0000`, 4 bytes big-endian. Comprobada con
`tools/aeon/aeonasm.py` (9 vectores en `KeyTableTests`).

## 2. Tablas código → función

| TV | Dónde | Formato | Teclas | Nombres de las funciones |
|---|---|---|---|---|
| LG | `0x39DD94` (solo con "Mando Soyea = sí") | `lista8`: u32 (0x0100\|código), función, 0,0,0 | 44 de 45 | `README_LG_MANDO_SOYEA.md` |
| ATEC | `0x422090` (la del original) | `directa256`: byte[código] = función, `FF` = nada | 48 | el rótulo del botón Soyea que la produce (tabla idéntica a la del Soyea `0x423CE0`) |
| Konka | `0x38FFA4` (la del original) | `lista4`: código, función, 0, 0; termina en función `FF` | 45 (máx. 45) | numeración = índices del LG (ver abajo) |

### Konka, cómo se encontró
1. El texto `"IR:%02dh"` (`0x3368E5`) lo usa `0x25FAB4`, que procesa la tecla ya traducida (RAM `0x3D039D`).
2. La traducción viene de `0x260B1F` → `0x2212C6` → `0x2A6307`: compara el cliente recibido (RAM `0x3CAD7A`) con el
   de `0x38FFA2` (modo 0, mando normal) o con `0x8890` (modo 1, mando de fábrica, tablas `0x38F79C`/`0x38FBA0`).
3. En modo 0 la tecla sale de la tabla RAM `0x3EA7D4` (8 bytes por código), que `0x25F519` rellena al arrancar
   desde la lista de flash `0x38FFA4` (entradas de 4 bytes hasta la primera con tecla `FF`, máximo `0x3903A4`).
4. La lista tiene 45 teclas y el terminador `FF FF 00 00` en `0x390058`; **detrás hay otros datos**, así que la
   tabla editable mide 46 entradas (45 teclas como máximo).
5. Numeración: dígitos `0x02`–`0x0B`, flechas `0x0D`–`0x10`, OK `0x11`, VOL/CH `0x12`–`0x15`, colores
   `0x29`–`0x2C`… igual que los índices del LG; además `0x25F971` convierte ARRIBA→CH+ (`0x0D`→`0x14`),
   DERECHA→VOL+ (`0x10`→`0x12`), igual que el LG. Las teclas que el LG no documenta salen como
   "tecla Konka 0xNN (sin identificar)". Solo se ofrecen las 45 funciones que ya usa el mando original.

## 3. En FirmReaper

- Perfiles: `clienteMando` y `tablaMando` (formatos en `docs/PERFILES.md`). LG v7, ATEC v4, Konka v4.
- Ventana: **Código de cliente** (casilla con el código; "Restaurar") y **Tabla del mando → Editar…**, que ahora
  permite **añadir códigos** de botones de otro mando. CLI: `--cliente "37 78"` y `--tabla-mando`.
- El nombre del archivo generado lleva `_cliente-XXXX` y/o `_tabla-editada` si se personalizó el mando.
- Por defecto no cambia nada: las versiones documentadas salen idénticas.

## Sin probar en el TV

Todo lo de esta etapa (cliente elegido y tablas editadas en los 3 TV). Para saber los códigos de un mando
desconocido hace falta leerlos (un receptor IR con un Arduino, por ejemplo); el Konka además los imprime por su
puerto serie de depuración (`"IR:%02dh"`), no comprobado.

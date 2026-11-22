# RCA (BD_MST030B_10AL8_12052) — mando Soyea: análisis y PRUEBAS v1–v3 (2026-11-22)

Original: `RCA_letra_roja_fondo_blanco_con_perros.BIN`, SHA-256 `669bdb5e3a8ad4bbe42639546671c560c47add9577d8a63d2f3f8397135aa307`.
Contenedor MStar válido; app en RAM `0x200000` (base `0x203800`), misma familia que el Konka (mismo controlador IR,
textos "IR:%02dh", "<Ir Resend…>"). Direcciones = RAM de la app.

## Mando (comprobado en el código)

| Qué | Dónde | Original |
|---|---|---|
| Código de cliente (en memoria `b2 b1`, como el Konka) | `0x398AB0` | `07 07` |
| Reposo (registros IR `0x3DA4`/`0x3DA5`) | `0x2A798A` / `0x2A7BC5` los calculan **desde `0x398AB0`** | — |
| Tecla que enciende desde reposo (RIU `0x7B64/65/6C/6D`) | `0x2A693E` `bt.movi r6,0x2` (fija) | `02` (v2: `0C`) |
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


## Prueba v2 (2026-11-22)

**Resultado de la v1 (usuario):** el Soyea apaga el TV pero **no lo enciende** desde reposo; las flechas, OK y otras
teclas no funcionan.

### Causas encontradas
1. **Encendido desde reposo.** La rutina de entrada en reposo (`0x2A68E3`, la que escribe "=> StandBy") copia bien el
   cliente desde `0x398AB0`, pero la **tecla que despierta el TV** (registros RIU `0x7B64`/`0x7B65`/`0x7B6C`/`0x7B6D`)
   está fija: `bt.movi r6,0x2` en `0x2A693E` (bytes `98 c2`) = código `02`, el POWER del mando RCA. El POWER del Soyea
   es `0C` → se cambia a `bt.movi r6,0xc` (`98 cc`, codificación comprobada con `tools/aeon/aeonasm.py`). Es el mismo
   tipo de fallo que tuvo el ATEC (allí era el cliente; aquí, la tecla).
2. **CH± con valores equivocados.** En la v1 CH+/CH− llevaban `0x19`/`0x18` (los de los botones arriba/abajo del mando
   RCA). La rutina `0x262F9B` (igual que en el Konka) usa `0x14`/`0x15` para CH± y `0x0D`–`0x10` para las flechas.
3. **Flechas.** El Soyea no tiene flechas propias: su cruceta envía VOL+ (`15`), VOL− (`16`), CH+ (`17`) y CH− (`18`).
   La conversión flecha↔VOL/CH del firmware (`0x262F9B`) solo se ejecuta en otro modo de entrada (modo 2), no con el
   mando normal. En la v2 la cruceta da VOL±/CH± y las **flechas de verdad** (`0x0D`–`0x10`) van en PAGE UP/PAGE DOWN
   (arriba/abajo) y PREV/NEXT (izquierda/derecha). La prueba dirá si los menús se mueven con la cruceta o solo con esos
   cuatro botones; si es lo segundo, la v3 pondrá las flechas en la cruceta.
4. **OK** ya tenía en la v1 el valor comprobado `0x11`; si sigue sin funcionar hace falta el resultado botón a botón
   (NO SÉ todavía por qué falló: puede que pareciera no funcionar porque el cursor no se movía).

### Archivo
Perfil: `docs/historial/pruebas/rca-prueba-mando-soyea.json` (versión 2). `rca-prueba-mando-soyea.BIN`, SHA-256
`e372b6066ddb28cb64bf5df10697fe6b45d38fbb256094c70ec3c4e2025b6d75`, CRC `0xE6BDA94A`. Cambian 76 bytes de la app:
cliente (2), lista (72 dentro de `0x398AB4`–`0x398B53`) y tecla de reposo (1 byte real de la instrucción). Las listas
de `0x398B54` en adelante, SBOOT, PM, TEL, recursos y base de datos quedan idénticos (verificado por FirmReaper y por
una comparación independiente en Python). Zopfli 5: 17 489 bytes libres.

Seguridad: el parche de reposo está en una zona permitida de 2 bytes (`0x2A693E`–`0x2A6940`) y se comprueba el valor
original (`98c2`) antes de escribir; si el firmware no es exactamente este, FirmReaper no genera nada.

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
| `15` | VOL+ (cruceta derecha) | `0x12` | comprobado | |
| `16` | VOL- (cruceta izquierda) | `0x13` | comprobado | |
| `17` | CH+ (cruceta arriba) | `0x14` | comprobado | |
| `18` | CH- (cruceta abajo) | `0x15` | comprobado | |
| `19` | PICTURE | `0x28` | supuesto | |
| `1A` | AUDIO | `0x23` | supuesto | |
| `1C` | INFO | `0x1B` | supuesto | |
| `1D` | EXIT | `0x1A` | supuesto | |
| `21` | ROJO | `0x29` | supuesto | |
| `22` | VERDE | `0x2A` | supuesto | |
| `23` | AMARILLO | `0x2B` | supuesto | |
| `24` | AZUL | `0x2C` | supuesto | |
| `28` | PAGE UP | `0x0D` | ARRIBA (comprobado) | |
| `29` | PAGE DOWN | `0x0E` | ABAJO (comprobado) | |
| `45` | SLEEP | `0x5E` | supuesto | |
| `46` | CC | `0x43` | supuesto | |
| `47` | SUBTITLE | `0x63` | supuesto | |
| `4B` | PLAY | `0x52` | supuesto | |
| `4C` | STOP | `0x53` | supuesto | |
| `4E` | PREV | `0x0F` | IZQUIERDA (comprobado) | |
| `53` | NEXT | `0x10` | DERECHA (comprobado) | |
| `54` | REW | `0x54` | supuesto | |
| `55` | FF | `0x55` | supuesto | |

Sin función en el Soyea: DASH (`0A`), FAV (`20`), TV/RADIO (`25`), MTS (`40`), CH.LIST (`49`), TEMP_DET (`4F`), EPG
(`51`), `56`, `57`. Se quitaron de la v1 los valores no comprobados `0x19`, `0x18`, `0x57` y `0x40` (sin nombre).

## Prueba v3 (2026-11-22)

**Resultado de la v2 (usuario):** encendido/apagado, OK, VOL y CH (cruceta) funcionan; **en el reproductor USB la
cruceta sigue cambiando volumen y canal**: no hay forma de moverse por los archivos.

### Cómo recibe las teclas el reproductor USB (comprobado en el código)
1. La tecla (RAM `0x3CA9C9`) pasa por `0x29908C`, que solo actúa en los estados ZUI 2–3 (`0x3D3764`), y por
   `0x2850D3`, un `switch` (tabla `0x32DEDC`) que la traduce a la **tecla virtual (VK)** de la interfaz:
   flechas `0x0D`/`0x0E`/`0x0F`/`0x10` → VK `0x0E`/`0x12`/`0x13`/`0x15`, OK `0x11` → VK `0x14`, dígitos
   `0x02`–`0x0B` → VK `0x16`–`0x1F`, POWER `0x01` → VK `0x0F`, y **`0x12` → VK `0x27`, `0x13` → VK `0x0C`,
   `0x14` → VK `0x0D`, `0x15` → VK `0x05`, `0x16` → VK `0x06`**.
2. La navegación por defecto de la ZUI (`0x2729DF`) mueve el foco solo con VK `0x0E` (arriba), `0x12` (abajo),
   `0x13` (izquierda) y `0x15` (derecha).
3. `0x2753D3` recibe el mensaje de tecla (VK en `+8`) y, en el estado 2, llama en `0x275411` a `0x284EDE`, que
   reparte según el subestado `0x3D3768`. El **subestado `0x12` es el reproductor USB** (`0x2A3619`, textos
   "VK_VOLUME_PLUS MINUS", "VK_CHANNEL_PLUS or VK_CHANNEL_MINUS"): ahí VK `0x0C`/`0x0D` suben/bajan el volumen
   (`0x19D` +1/−1) y VK `0x05`/`0x06` son canal. Las flechas siguen su propia lógica del reproductor.

### Cambio de la v3
- `0x275411`: `bg.jal 0x284EDE` → `bg.jal 0x341112`.
- `0x341112` (78 bytes): si el subestado es `0x12` (reproductor USB), cambia en el mensaje VK `0x27` → DERECHA
  (`0x15`), `0x0C` → IZQUIERDA (`0x13`), `0x0D` → ARRIBA (`0x0E`), `0x05` → ABAJO (`0x12`) — los cuatro VK que da
  la cruceta del Soyea con la tabla de la v2 —; después sigue en `0x284EDE` igual que el original. Solo usa r3 y
  r7 (`0x284EDE` machaca r7 al entrar); r10 (el mensaje) lo conserva la función que llama.
- Hueco: textos de depuración del decodificador JPEG/MPO (`"Progressive image size too big..."`, `"APP2"`,
  `"_pu8In_buf_ofs = %lx"`, `"num of entry = %d"`): **ninguna** instrucción `addi`/`ori` usa sus direcciones y
  ningún dato de 32 bits apunta a ellos (comprobado en todo el programa). El texto siguiente (`0x341194`) no se toca.
- Rutina generada y verificada (desensamblada de vuelta) con `tools/aeon/build_cave_rca_v3.py`.
- Fuera del reproductor USB no cambia nada: viendo la TV y en los menús la cruceta sigue como en la v2.

**Efecto esperado (sin probar):** en el reproductor USB la cruceta solo se desplaza. Durante la reproducción
izquierda/derecha harán lo que hagan las flechas del mando original en el reproductor, **no el volumen** (mismo
compromiso que la v3 del LG). Si hace falta el volumen al reproducir, se afinará como la v5 del LG.

**Duda abierta:** según la traducción a VK y la lista de teclas con repetición (`0x2634D0`: flechas, `0x13`–`0x16`,
`0x1C`), en este firmware VOL+/VOL−/CH+/CH− podrían ser `0x13`/`0x14`/`0x15`/`0x16` y no `0x12`–`0x15` como en el
Konka. Con la tabla actual, en los menús el botón VOL− del Soyea daría "volumen +" y CH+ "volumen −". Se pide al
usuario comprobar el sentido de cada botón; si sale cruzado, la v4 corrige la tabla.

Archivo: `rca-prueba-mando-soyea.BIN`, SHA-256 `ee56f24c659d2de4404439184a285c916488f8c4107b9bed8140191fcc9d0190`,
CRC `0x05D3521E`. Cambian 152 bytes de la app (cliente 2, lista 73, reposo 1, gancho 3, rutina 73); nada más.

## Resultado de la v3 y diagnóstico v4 (2026-11-22)

**El mando real no es el Soyea LKK32CMB:** es un universal **GSSG KT1440** (foto del usuario), que tiene cruceta
propia **y** botones VOL±/CH± aparte. El usuario quiere la cruceta solo como flechas y VOL/CH en sus botones.

**Resultado de la v3 (usuario):** PLAY no funciona; la flecha arriba hace de abajo, la derecha hace de izquierda;
abajo e izquierda no hacen nada.

Interpretación (sin confirmar):
- Con la v3, arriba llega al reproductor como VK `0x0E` y derecha como VK `0x15`, tanto si la cruceta envía los
  códigos de VOL/CH (rutina) como si envía PAGE UP/NEXT (tabla). Que arriba baje y derecha vaya a la izquierda
  indica que **los nombres de dirección que di a la navegación (`0x2729DF`) están invertidos**: probablemente VK
  `0x0E` = abajo, `0x12` = arriba, `0x13` = derecha, `0x15` = izquierda ("no funcionaron" = el cursor ya estaba en
  el borde). Esto también afecta a la numeración de las flechas en la lista (`0x0D`–`0x10`).
- No se sabe si la cruceta del KT1440 envía los mismos códigos que VOL±/CH± (como el Soyea LKK32CMB, que no tiene
  flechas) o códigos propios. **Si son los mismos, el TV no puede distinguirlos** y lo pedido no es posible.

**Diagnóstico v4** (`docs/historial/pruebas/rca-diagnostico-mando-v4.json`, `rca-DIAGNOSTICO-mando.BIN`,
SHA-256 `bde10ef79e19629dbb263d5ce8466bf9bd2e1a043537952d7e5d0e82ac5980a2`, CRC `0x87F71F82`, 80 bytes
cambiados): sin la rutina de la v3. La lista solo deja POWER, OK, MENU y EXIT, y convierte los códigos dudosos en
dígitos (el TV muestra el número):

| Código | Número | Código | Número |
|---|---|---|---|
| `15` (VOL+ Soyea) | 1 | `29` (PAGE DOWN) | 6 |
| `16` (VOL−) | 2 | `4E` (PREV) | 7 |
| `17` (CH+) | 3 | `53` (NEXT) | 8 |
| `18` (CH−) | 4 | `4B` (PLAY) | 9 |
| `28` (PAGE UP) | 5 | los otros 25 códigos Soyea conocidos | 0 |

Los dígitos del mando (`00`–`09`) no están en la lista para no confundir. Con el resultado se hará la v5.

**Resultado del diagnóstico v4 (usuario):** mando configurado con el código **010 (SOYEA DTV)**. Ni en USB ni en
TV apareció ningún número. Fallo del diseño del diagnóstico: se dio por hecho que este TV muestra en pantalla el
número marcado, y no está comprobado (sin canales sintonizados o fuera de la fuente TV puede no mostrar nada). El
resultado no dice nada sobre los códigos. Antes de otro diagnóstico se pregunta al usuario cómo se comportan, con la
v2/v3 y viendo la TV, los botones VOL±/CH± propios frente a la cruceta. Si hace falta, el siguiente diagnóstico
usará la barra de volumen (VOL± sí está comprobado), que se ve en cualquier fuente.

### Diagnóstico 2 (v5)
El usuario no puede responder las preguntas sin probar, así que se descarta por pasos usando solo efectos
comprobados que se ven en cualquier fuente: barra de volumen (valores `0x12`/`0x13`, comprobados en la v2) y
apagado (`0x01`, comprobado). Lista: `0C`→POWER, `15`→`0x12`, `16`→`0x13`, `17`→`0x01` (apaga); nada más (`18`
y todos los demás códigos no hacen nada). Perfil `docs/historial/pruebas/rca-diagnostico2-mando-v5.json`;
`rca-DIAGNOSTICO2-mando.BIN`, SHA-256 `e616e6e6522ac3167625c055d71ebbffe2628cc81bb69f948738acdfe3a241ff`,
CRC `0x3C027F55`, 80 bytes cambiados (cliente, lista y tecla de reposo).

Lectura esperada: si la cruceta da derecha=SUBE, izquierda=BAJA, arriba=APAGA, abajo=NADA, envía los códigos del
Soyea `15`–`18`. Si VOL±/CH± dan lo mismo, **son los mismos códigos** y el TV no puede separarlos. Si dan NADA,
son otros códigos y se buscarán en el diagnóstico siguiente.

**Resultado del diagnóstico 2 (usuario):** VOL+/VOL− y derecha/izquierda de la cruceta mueven la barra de volumen;
abajo de la cruceta no hace nada; arriba apaga el TV (y no lo enciende: normal, en reposo solo despierta el código
`0C`); PLAY no hace nada (normal, no estaba en la lista). **CH+ también apaga el TV** (confirmado después): mismo código `17` que arriba.

Conclusión: con el código **010** del KT1440, la cruceta envía los códigos del Soyea: derecha `15`, izquierda `16`
(**los mismos que VOL+/VOL−**), arriba `17` (**el mismo que CH+**, confirmado), abajo `18` (casi seguro el mismo que CH−).
El TV recibe exactamente lo mismo, así que **no puede distinguir la cruceta de VOL/CH**: lo pedido no es posible
con el código 010.

Siguiente paso propuesto: probar el KT1440 en el código **004/005 (Konka)**. El mando Konka original (cliente
`07 07`, igual que el RCA original) tiene códigos propios para las flechas (`60 61 65 62`), OK (`68`), VOL (`07 0B`)
y CH (`12 10`). La lista del RCA original ya usa `60 61 65 62 68 07 0B` con ese mismo cliente, así que la prueba
se hace con el **firmware original**, sin modificar.

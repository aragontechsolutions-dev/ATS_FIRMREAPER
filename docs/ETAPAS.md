# Plan por etapas

Inicio: 2026-11-15. Cada etapa se cierra con: código + pruebas automáticas + revisión de seguridad +
documentación, sin romper lo que ya funciona.

| # | Etapa | Estado |
|---|---|---|
| 1 | Motor de lectura y verificación (sin modificar nada) | ✅ Completada |
| 2 | Zopfli en C#, perfiles, generar idéntico a los scripts, escritura segura | ✅ Completada |
| 3 | Logo del Konka, editor de la tabla del mando del LG, visor hexadecimal | ✅ Completada |
| 4 | Perfil ATEC (panel 1280x800, 6/8 bits) + port de zlib | ✅ Completada |
| 5 | Manual de usuario y ejecutable final | ✅ Completada |
| 6 | Logo de arranque en ATEC y LG (girado / propio) | ✅ Completada |
| 7 | Panel: resolución y doble canal (ATEC; localizar en LG y Konka) | Pendiente |
| 8 | Mando: editor genérico (código de cliente + tabla) en los 3 | ✅ Completada |
| 9 | Espejo (mirror): ATEC desde cero; revisar el del OSD del LG | ✅ Completada |

## Etapa 1 — completada

- Solución .NET 10: `FirmReaper.Core` (motor), `FirmReaper.App` (WPF), `FirmReaper.Cli`, pruebas.
- CRC-32 MSB; descompresor deflate propio; cabecera, TEL, MS_IMG_INFO; 11 comprobaciones (12 desde la etapa 6).
- Catálogo incrustado e identificación (original / nuestra / referencia / no reconocido).
- Hallazgos: MS_IMG_INFO guarda la longitud de la app (no se comprueba al arrancar); el 3.er binario
  recibido es el **Soyea**; los 2 bytes finales de la cola no son un CRC16 conocido.

## Etapa 2 — completada

**Hecho**
- **Port de Zopfli a C#** (`Compression/Zopfli/`), fiel al C de py-zopfli 0.4.3.
- **`ExactLog`**: logaritmo determinista idéntico al de glibc (ver más abajo).
- **Perfiles JSON** (`docs/PERFILES.md`): integrados + carpeta externa `perfiles\`; validación estricta.
- **Generación** (`Patching/FirmwareBuilder.cs`) con 7 comprobaciones; **escritura atómica** sin sobrescribir,
  **copia de seguridad** de solo lectura del original e **informe** (texto + JSON) por cada archivo.
- CLI: `firmreaper-cli perfiles` y `firmreaper-cli generar`. Ventana: pestaña **Generar** (opciones, progreso, cancelar).
- Perfil **LG** (mando Soyea sí/no × espejo original/desactivado) y perfil **Konka** (MIRROR en menú, logo sin girar).
- Herramientas AEON en `tools/aeon` (ensamblador/desensamblador Python de referencia).

**Comprobado**
- El programa genera las 2 versiones del LG v5 **idénticas byte a byte** a las de `patch_ir_lg.py`
  (`17918f8f…`, `3a836aee…`), en ~10 s.
- Zopfli C#: 7 vectores de Python idénticos + la app real del LG.
- `aeon_isa.json` idéntico al de eb4x/ghidra-plugin-aeon@788cf8a; `build_cave_v2.py` reproduce la rutina del LG.
- El perfil Konka genera un archivo **idéntico** al de `patch_mirror_menu.py --sin-logo` (`3bffdfd0…`).
- Original Konka recibido: `TV4-32K16.bin` = `32c52965…` (el esperado). Regenerando con el script la versión
  con logo sale `81bd3c8b…160b93570`, la misma del README del proyecto (Pillow 12.3.0, libjpeg-turbo 3.1.4.1).
- GitHub Actions (Windows): compilación y pruebas superadas, incluida la del logaritmo.
- **Konka "espejo fijo"** (`TV4-32K16_MIRROR.bin`, `98a919fb…`, recibido después): sus 15 parches se obtuvieron
  comparándolo con el original y desensamblándolos (`bMirror = 1` + 14 lecturas de las banderas de espejo del
  GOP fijadas a 1). El perfil Konka (v2) tiene la opción **Modo: en el menú / fijo** y genera ambos idénticos.
- 107 pruebas superadas (Linux, con firmwares).

**Problema encontrado y resuelto: el logaritmo de Windows**
- GitHub Actions mostró que `Math.Log` en Windows da distinto último bit que Linux en algunos enteros →
  la compresión podría salir distinta en Windows.
- Además, glibc (Linux, lo que usó Python) **no redondea correctamente** en 57 enteros de 1..2 100 000.
- Solución: `ExactLog` calcula el valor correctamente redondeado con aritmética doble-doble (solo
  operaciones IEEE básicas, igual en cualquier CPU/SO) y aplica una tabla con esas 57 excepciones de glibc.
  Resultado: igual a glibc bit a bit en todo el rango que usa Zopfli.

**No comprobado / pendiente**
- El editor del mando del LG → etapa 3.
- La ventana no se ha abierto en un Windows real desde aquí (solo se compila).

## Etapa 3 — completada

### 3a. Logo del Konka — completada
- Perfil Konka v3: opción **Logo de arranque**: girado 180° (por defecto) / sin cambios / propio.
- Con las opciones por defecto (MIRROR en menú + logo girado) el programa genera **idéntico** el firmware
  probado en el TV (`81bd3c8b…160b93570`).
- Cómo: decodificación con LibJpeg.NET (mismos píxeles que Pillow, comprobado) + **codificador JPEG propio**
  que replica libjpeg-turbo (Pillow 12.3.0 / libjpeg-turbo 3.1.4.1): sale el mismo JPEG byte a byte. No hace
  falta ninguna biblioteca nativa (era la opción 1 recomendada; esta es mejor y se comprobó).
- Logo propio: validado (baseline, 1366×768, ≤ 131 381 bytes, sin basura, se decodifica); CLI `--logo`,
  ventana: se pide el JPEG al generar.
- 122 pruebas superadas.

### 3b. Editor del mando del LG y visor hexadecimal — completada
- Perfil LG: `tablaMando` con los 48 botones del Soyea y 53 funciones del LG. Nombres: los documentados en
  `README_LG_MANDO_SOYEA.md` + las 4 flechas de la rutina de la cruceta; 5 índices del mando LG original
  sin nombre documentado aparecen como "función LG 0xNN (sin identificar)" (no se inventan nombres).
- Ventana: pestaña Generar → "Tabla del mando: Editar…" (restaurar, importar, exportar). CLI:
  `firmreaper-cli tabla-mando --perfil … --salida tabla.json` y `generar … --tabla-mando tabla.json`.
- Seguridad: la tabla editada solo sustituye los bytes nuevos del parche de la tabla (misma dirección y
  longitud, dentro de su zona); se rechazan botones repetidos, funciones o botones no permitidos y más de 44 teclas.
- Comprobado: cambiar SLEEP → INPUT_SOURCE cambia 1 solo byte respecto a la v5 (`0x39DE98`).
- Pestaña **Cambios (hex)**: tras generar, cada parche en hexadecimal (original | nuevo) con los bytes
  cambiados en rojo. (No incluye desensamblado: posible mejora futura con las tablas de `tools/aeon`.)
- 133 pruebas superadas.

### Corrección: el .exe descargado abría una consola y se cerraba (2026-11-15)
- **Causa:** el workflow publicaba `FirmReaper.exe` (ventana) y `firmreaper.exe` (consola) en la MISMA carpeta;
  Windows no distingue mayúsculas, así que el segundo sobrescribía al primero. El usuario ejecutaba en realidad
  la línea de comandos, que sin parámetros muestra la ayuda y termina.
- **Arreglo:** la línea de comandos pasa a llamarse `firmreaper-cli.exe`; cada uno se publica en su carpeta;
  GitHub Actions comprueba que `FirmReaper.exe` es de tipo ventana y lo **arranca** (debe seguir abierto 20 s).
- La ventana ahora muestra y registra cualquier error inesperado (`%LOCALAPPDATA%\FirmReaper\errores.log`);
  la consola abierta con doble clic explica qué es y espera a Enter.

## Etapa 4 — completada

- **Port de zlib 1.3** a C# (`Compression/ZlibDeflater.cs`, solo `deflate_slow`): 9 vectores de Python idénticos y
  la app real del ATEC (8 y 6 bits) recomprimida igual que en los `.BIN`.
- Perfiles: `compresion` admite `zlib` con `nivel` (4–9).
- **Perfil ATEC** (generado con `patch_entry()` de `patch_panel.py`): opción **Salida LVDS**: 8 bits (por
  defecto, "FUNCIONÓ") / 6 bits + dither ("NO funcionó"). Un solo parche de 92 bytes (la entrada #1 de la tabla
  de paneles, RAM `0x4E3A78`); zona permitida = esa entrada.
- Generado con el programa: 8 bits = `ab3d3485…`, 6 bits = `7bd55c39…` (idénticos a `patch_panel.py`), ~1 s.
- **Sin confirmar:** que el `Atec32L18D_LP141WX1_8bit.BIN` grabado en el TV tenga esa misma huella (lo generó
  otra sesión; si usó otra zlib, sus bytes comprimidos podrían diferir aunque el contenido sea el mismo).
- 150 pruebas superadas.

## Etapa 5 — completada

- **Manual de usuario:** `docs/MANUAL_USUARIO.md` y `docs/MANUAL_USUARIO.pdf` (A4, 8 páginas). Cubre instalación,
  la ventana, generar (LG, Konka, ATEC, editor del mando, logo propio), archivos que se guardan, cómo grabar con
  el programador, mensajes de error, línea de comandos, perfiles y versiones conocidas.
- El PDF se regenera con `python3 tools/docs/md_a_pdf.py docs/MANUAL_USUARIO.md docs/MANUAL_USUARIO.pdf`
  (markdown 3.7 + Chromium/Playwright).
- **Versión 1.0.0** (en `Directory.Build.props`): se muestra en el título de la ventana y en la ayuda del CLI.
- El paquete de GitHub Actions incluye `FirmReaper.exe`, `firmreaper-cli.exe`, `MANUAL_USUARIO.pdf`,
  `SHA256.txt` y `LEEME.txt`.
- La guía técnica para crear perfiles ya existía (`docs/PERFILES.md`).

### Publicación (Release)
- Al fusionar en `main`, GitHub Actions publica la Release `vX.Y.Z` (versión de `Directory.Build.props`) si no
  existe, con `FirmReaper.exe`, `firmreaper-cli.exe`, `MANUAL_USUARIO.pdf`, `SHA256.txt` y un `.zip` con todo.
  Para una versión nueva: cambiar `<Version>`, añadir sus notas y fusionar.

### Icono (2026-11-15)
- `src/FirmReaper.App/Assets/FirmReaper.ico` (16–256 px): chip de memoria con marca de verificación. Se usa en el
  `.exe` de la ventana, en sus ventanas y en `firmreaper-cli.exe`. Se regenera con `tools/icono/generar_icono.py`.

### ATEC: mando Soyea (2026-11-15)
- El ATEC y el Soyea comparten SBOOT/PM (idénticos), rutina del mando y tabla código→función (idéntica). Solo
  cambia el código de cliente (`0x7736` → `0x7837`), en una instrucción (`0x3233D5`). Detalle:
  `docs/historial/README_ATEC_MANDO_SOYEA.md`.
- Perfil ATEC v2: opción **Mando** (ATEC original por defecto / Soyea). Sin probar en el TV.
- 152 pruebas superadas.

## Versión 1.1.0
- Icono propio y opción de mando Soyea en el ATEC (ver arriba). La 1.0.0 se publicó al fusionar el PR #1,
  antes de estos dos cambios. Notas: `docs/NOTAS_VERSION_1.1.0.md`.
- El trabajo `release` usa ahora las notas de la versión (`docs/NOTAS_VERSION_<versión>.md`) y falla si faltan.

## Etapa 6 — completada (logo de arranque en ATEC y LG)

Objetivo: las 5 funciones (mando, espejo, logo, resolución, doble canal) para los 3 firmwares conocidos.
Esta etapa cubre el **logo**. Análisis: `docs/historial/README_LOGOS_ATEC_LG.md`.

**Hallazgos**
- Cada recurso TEL termina en un **CRC-16** (0x8005, init 0) — antes se desconocía. El arranque no lo comprueba.
- El campo `0x22088` de la TEL es el **fin del área de recursos** (no un "tamaño reservado").
- ATEC: el logo es siempre el recurso `0x0300` (2167x1228, 4:2:0).
- LG: el logo "LG Life's Good" está **fuera de la TEL**, en `0x370000` (zona del "logo capturado", 1376x768, 4:2:2);
  se usa si el ajuste de logo vale 2; si no, el TV muestra el recurso `0x0301` (azul liso).
- LibJpeg.NET **no** decodifica igual que Pillow en 4:2:0/4:2:2 (la afirmación de la etapa 3 solo era cierta en
  4:4:4). Corregido.

**Hecho**
- Codificador JPEG con submuestreo 4:2:2 y 4:2:0 idéntico a libjpeg-turbo (24 vectores de Pillow).
- Decodificación idéntica a libjpeg-turbo (ensanchado y color propios).
- Perfiles: `logo` con `idTel` (+ `crc16`) o `zonaFlash`. ATEC v3 y LG v6 con opción **Logo de arranque**:
  sin cambios (por defecto: las versiones conocidas salen idénticas) / girado / propio.
- Logo propio: **cualquier imagen JPEG, PNG o BMP**; se encaja sin deformar, se codifica en el formato del original
  y, si no cabe, se baja la calidad por pasos. Un JPEG con el formato exacto se usa tal cual.
- Nueva comprobación del análisis: "CRC-16 de los recursos" (12 en total).
- Revisión de seguridad: `docs/ARQUITECTURA.md` (etapa 6).

**Comprobado**
- Logos del ATEC y del LG girados = Pillow 12.3.0 byte a byte; Konka sigue generando `81bd3c8b…`.
- ATEC: solo cambia el recurso `0x0300` y su CRC-16 queda bien; LG: solo cambia `0x370000–0x3B0000`.
- Prueba de extremo a extremo con la línea de comandos (PNG de 800x600 → ATEC y LG) revisada a la vista.
- 198 pruebas superadas.

**Sin probar en el TV:** logos del ATEC y del LG. Confirmado por el usuario (2026-11-16): el LG muestra
"LG Life's Good" al encender, así que el logo que cambia el programa es el que se ve.

## Etapa 8 — completada (mando genérico en LG, ATEC y Konka)

Análisis: `docs/historial/README_MANDO_GENERICO.md`. (La etapa 7, panel, espera datos del usuario: modelo del panel
Full HD y fotos de la placa.)

**Hallazgos**
- Konka: código de cliente `07 07` en `0x38FFA2` y lista código→tecla (45 teclas) en `0x38FFA4`; su numeración de
  teclas es la de los índices del LG.
- Orden de los bytes del cliente comprobado en los 3 (LG y Konka invertido; ATEC inmediato `b2·256 + b1`).
- ATEC: el inmediato de `bg.sfeqi` tiene signo → solo códigos con el 2.º byte ≤ 7F.
- Error propio detectado por la validación nueva: había supuesto que el LG guardaba el cliente en el orden de envío;
  el parche probado (`bf00` → `7837`) demuestra que va invertido. Corregido antes de publicar.

**Hecho**
- Tablas en 3 formatos (`lista8`, `lista4`, `directa256`), ligadas a un parche (LG) o editadas en su sitio (ATEC, Konka).
- Código de cliente editable en los 3 (`clienteMando`), con su codificación por firmware.
- Editor: añadir códigos de otro mando; nota propia de cada perfil. Ventana: casilla "Código de cliente". CLI: `--cliente`.
- Nombre del archivo con `_cliente-XXXX` / `_tabla-editada`. Perfiles: LG v7, ATEC v4, Konka v4.
- Eliminado el formato de cliente "bytes" (orden directo): ningún firmware lo usa.
- Revisión de seguridad: `docs/ARQUITECTURA.md` (etapa 8). 230 pruebas.

**Sin probar en el TV:** códigos de cliente elegidos y tablas editadas en los 3.

## Etapa 9 — completada (espejo en ATEC y LG)

Análisis: `docs/historial/README_ESPEJO_ATEC_LG.md`.
- **ATEC y LG ya tienen el espejo (imagen + menús) en su menú de fábrica**: ATEC "Mirror Enable", LG "MIRROR".
  No hace falta parche. Konka: ya resuelto antes.
- LG: la opción "Espejo: desactivado" solo cambia el valor inicial; el ajuste MIRROR del menú se aplica en el arranque
  y manda. Se cambió el texto de la opción (mismos bytes, salidas documentadas idénticas). Perfil LG v8.
- Nada nuevo que escribir en el firmware → sin riesgos nuevos de seguridad. Sin probar en el TV.

## Corrección (2026-11-22): ATEC + mando Soyea no encendía desde reposo

Probado por el usuario con el archivo de la 1.1.0 (la 1.2.0 tenía el mismo fallo): el mando funcionaba recién grabado pero no encendía el TV tras
apagarlo. Causa: el código de cliente también está, byte a byte, en la inicialización del receptor IR por hardware
(registros `0x3DA4`/`0x3DA5`, filtro de reposo), y no se cambiaba. Corregido: perfil ATEC v5 (2 bytes más:
`0x32394C`/`0x323957`, iguales que en el Soyea), campos `reposo` en `clienteMando` y nueva regla de coherencia al
cargar perfiles. LG y Konka no tienen el problema. Detalle: `docs/historial/README_ATEC_MANDO_SOYEA.md`.
243 pruebas.

## Versión 1.2.0
- Etapas 6, 8 y 9. Publicada al fusionar el PR #3 (2026-11-18), antes de la corrección del reposo del ATEC.
  Notas: `docs/NOTAS_VERSION_1.2.0.md`.

## ATEC: encendido desde reposo confirmado + opción de resolución (1.3.0, 2026-11-22)

- El usuario probó `8e797d5d…`: el mando Soyea funciona, apaga y **enciende desde reposo**. Faltaba la rutina de
  entrada en reposo (`0x2430F1`), que escribe el código fijo en el código de arranque sin comprimir.
- Generador: parches en el código de arranque sin comprimir (flash `0x23080`–`0x23880`) con CRC recalculado.
- Perfil ATEC v6: opción **Resolución del panel** (1366x768 original, 1280x800 LP141WX1, 1280x720, 1024x768, 800x600)
  separada de **Salida LVDS** (8/6 bits); mando Soyea en 5 sitios; código de cliente con 4 campos de reposo.
  Por defecto: resolución original. Las versiones documentadas siguen saliendo idénticas.
- Detalle: `docs/historial/README_ATEC_RESOLUCIONES.md` y `README_ATEC_MANDO_SOYEA.md`. 252 pruebas.

## Versión 1.2.1
- Corrección del encendido desde reposo con el mando Soyea (o un código elegido) en el ATEC (ver arriba).
  Notas: `docs/NOTAS_VERSION_1.2.1.md`.

## Versión 1.3.0
- Encendido desde reposo del ATEC con el mando Soyea (probado) y opción de resolución del panel.
  Notas: `docs/NOTAS_VERSION_1.3.0.md`.

## Placa LM2SA V1.6: dos firmwares nuevos (1.4.0, 2026-11-23)

- Firmwares del usuario `MST_EXT_SPI_ISP_20210531_113958.BIN` y `..._20210614_165953.BIN`: mismo programa base que
  el ATEC (ID `BD_MST061B_10AZU_14014`), distintas direcciones y versiones (2015 / 2014).
- Generador: la parte del ATEC pasa a ser una función común (`perfil_familia_061b`) para los tres; el perfil del
  ATEC sale idéntico.
- Perfiles `lm2sa-v16-20210531` y `lm2sa-v16-20210614`: resolución, bits, mando (Soyea en 5 sitios), logo
  (720x480), código de cliente y tabla editables; espejo en el menú de fábrica ("Mirror Enable"). Zopfli 5.
- Catálogo: los dos originales. Detalle: `docs/historial/README_LM2SA_PERFILES.md`. 256 pruebas.

## Versión 1.4.0
- Perfiles de la placa LM2SA V1.6. Notas: `docs/NOTAS_VERSION_1.4.0.md`.

## RCA (mando): en pausa (2026-11-23)
- La tabla del Konka (KT1440 código 005) funciona salvo ►II; pruebas E1/E2 enviadas sin resultado. Ver
  `docs/historial/README_RCA_MANDO_SOYEA.md`. No hay perfil integrado del RCA todavía.

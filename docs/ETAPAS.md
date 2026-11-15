# Plan por etapas

Inicio: 2026-11-15. Cada etapa se cierra con: código + pruebas automáticas + revisión de seguridad +
documentación, sin romper lo que ya funciona.

| # | Etapa | Estado |
|---|---|---|
| 1 | Motor de lectura y verificación (sin modificar nada) | ✅ Completada |
| 2 | Zopfli en C#, perfiles, generar idéntico a los scripts, escritura segura | ✅ Completada |
| 3 | Logo del Konka, editor de la tabla del mando del LG, visor hexadecimal | ✅ Completada |
| 4 | Perfil ATEC (panel 1280x800, 6/8 bits) + port de zlib | ✅ Completada |
| 5 | Manual de usuario y ejecutable final | Pendiente |

## Etapa 1 — completada

- Solución .NET 10: `FirmReaper.Core` (motor), `FirmReaper.App` (WPF), `FirmReaper.Cli`, pruebas.
- CRC-32 MSB; descompresor deflate propio; cabecera, TEL, MS_IMG_INFO; 11 comprobaciones.
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

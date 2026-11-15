# Zopfli (raw deflate) y manejo de JPEG (logo 1366x768, 4:4:4, DRI 171) desde C# .NET 8 WPF single-file

Fecha de las notas: 2026-11-15. Restricciones de red de la sesión: no pude acceder a learn.microsoft.com, docs.sixlabors.com, man.archlinux.org ni a la API de GitHub (los repos no estaban habilitados). Lo que depende de esas fuentes se marca **[NO VERIFICADO]** o se cita vía resultados de búsqueda/espejos. El código fuente de py-zopfli 0.4.3 SÍ se descargó (sdist de PyPI) y se inspeccionó localmente.

## 1. Zopfli para .NET: paquetes, ports, determinismo y raw deflate

### Takeaway
No existe un port C# puro y mantenido de Zopfli; el único wrapper en NuGet (libzopfli-sharp, 2014) es viejo pero expone `ZOPFLI_FORMAT_DEFLATE`. Zopfli es totalmente determinista (PRNG con semilla fija), así que compilar el mismo código C que trae py-zopfli 0.4.3 en una DLL nativa propia (o portarlo a C# fielmente) y llamar a `ZopfliCompress(..., ZOPFLI_FORMAT_DEFLATE, ...)` con `numiterations=5, blocksplitting=1, blocksplittinglast=0, blocksplittingmax=15` debería dar bytes idénticos a lo que hace el prototipo Python (zlib de Zopfli sin cabecera de 2 bytes ni Adler-32 de 4 bytes).

### Cited Findings
- **libzopfli-sharp** (echovoice): "C# wrapper for the Zopfli and ZopfliPNG Compression Algorithm", licencia Apache-2.0; `ZopfliStream` acepta `ZopfliFormat` con `ZOPFLI_FORMAT_GZIP`, `ZOPFLI_FORMAT_ZLIB`, `ZOPFLI_FORMAT_DEFLATE`; `ZopfliOptions` expone `numiterations`, `blocksplitting` (def. 1), `blocksplittinglast` (def. 0), `blocksplittingmax` (def. 15) — [GitHub echovoice/libzopfli-sharp](https://github.com/echovoice/libzopfli-sharp)
- Versión NuGet 1.0.14325.1; última actualización 2014 (sin mantenimiento) — [NuGet libzopfli-sharp](https://www.nuget.org/packages/libzopfli-sharp); [nugetmusthaves](https://nugetmusthaves.com/Package/libzopfli-sharp)
- Ejemplo de uso del formato DEFLATE con `MemoryStream` — [coderwall (espejo)](https://scriptagc.wasmer.app/https_coderwall_com/p/khjbmq/use-zopfli-compression-in-your-cdn)
- "Zopfli.Net" aparece en una guía de terceros con `ZopfliGZip.Compress`; no encontré su página en NuGet ni confirmación de soporte raw deflate — [ssojet](https://ssojet.com/compression/compress-files-with-zopfli-in-csharp)
- No encontré ningún port C# puro (managed) de Zopfli; las opciones C# son wrappers del C nativo. El upstream es C (google/zopfli) — [google/zopfli](https://github.com/google/zopfli)
- **py-zopfli (paquete PyPI `zopfli`)**: última versión 0.4.3 (10-jun-2026), Apache-2.0, Python ≥3.10 — [PyPI zopfli](https://pypi.org/project/zopfli/)
- El repo fonttools/py-zopfli tiene el submódulo `zopfli` fijado al commit `400a50779d7272b044f0ae339c5576c0e842d79e` del fork **fonttools/zopfli** (no google/zopfli directamente) — [GitHub fonttools/py-zopfli](https://github.com/fonttools/py-zopfli)
- Inspección local del sdist `zopfli-0.4.3.tar.gz` (src/zopflimodule.c): `zopfli.zopfli.compress(data, verbose, numiterations=15, blocksplitting=1, blocksplittinglast=0, blocksplittingmax=15, gzip_mode=0)`; `output_type = gzip_mode ? ZOPFLI_FORMAT_GZIP : ZOPFLI_FORMAT_ZLIB` → **nunca emite raw deflate directamente**; por eso el prototipo "pela" el contenedor zlib — [sdist PyPI zopfli 0.4.3](https://pypi.org/project/zopfli/)
- En el mismo sdist, `ZopfliZlibCompress` escribe cabecera `cmf=120, flevel=3` (= bytes `0x78 0xDA`), luego `ZopfliDeflate(options, 2 /*dynamic*/, 1 /*final*/, ...)` y al final Adler-32 big-endian (4 bytes). `ZopfliCompress` con `ZOPFLI_FORMAT_DEFLATE` llama a exactamente el mismo `ZopfliDeflate(options, 2, 1, ..., &bp=0, ...)`. ⇒ `zlib_out[2:-4] == deflate_out` byte a byte — [sdist PyPI zopfli 0.4.3](https://pypi.org/project/zopfli/) (zopfli/src/zopfli/zlib_container.c, zopfli_lib.c)
- Determinismo: `squeeze.c` usa un PRNG propio con semilla fija (`InitRanState`: `m_w = 1; m_z = 2`), sin dependencia de tiempo/hilos → misma entrada + mismas opciones + mismo código = misma salida — [sdist PyPI zopfli 0.4.3](https://pypi.org/project/zopfli/)
- SHA-256 de los .c bundled en py-zopfli 0.4.3 (para comparar con la copia que se compile en .NET): deflate.c `5b35044b…7425`, squeeze.c `71e6c183…49a3`, lz77.c `c2d9f5b4…67b6`, blocksplitter.c `e75ecc73…9c20`, tree.c `5ecbbffe…63`, katajainen.c `a06e74fd…bd77`, cache.c `93b0aa53…2874`, hash.c `e0c8ea7a…d5`, util.c `84bb4c5e…26`, zopfli_lib.c `04767d65…9d71` (calculados localmente) — [sdist PyPI zopfli 0.4.3](https://pypi.org/project/zopfli/)
- Constantes relevantes: `ZOPFLI_WINDOW_SIZE 32768`, `ZOPFLI_MASTER_BLOCK_SIZE 1000000` (entradas >1 MB se procesan en bloques maestros) — sdist (util.h)

### Inferences
- Opción recomendada A (máxima fidelidad): compilar los .c de `zopfli/src/zopfli/` **del sdist py-zopfli 0.4.3** (mismo commit 400a507) como `zopfli.dll` x64 (MSVC o clang-cl), exportar `ZopfliCompress`/`ZopfliInitOptions`, P/Invoke con `ZOPFLI_FORMAT_DEFLATE (=2)`. Verificar contra golden files. Posible fuente de no-determinismo entre compiladores: aritmética de punto flotante en el cálculo de costes (`squeeze.c`/`deflate.c` usan `double`); con x64 SSE2 y sin `/fp:fast` debería coincidir con el build de CPython/manylinux/win wheels, pero **[NO VERIFICADO]** — probar.
- Opción B: portar Zopfli a C# (~3–4 k líneas de C, sin dependencias). Factible y elimina la DLL; la igualdad byte-a-byte depende de replicar exactamente los `double` (C# `double` es IEEE-754 binario64, igual que C x64 SSE2; evitar `Math.FusedMultiplyAdd` y comprobar `log` — Zopfli usa `log()` en `ZopfliCalculateEntropy`; `Math.Log` de .NET llama al CRT y normalmente coincide, **[NO VERIFICADO]**). Validar con golden files.
- libzopfli-sharp no es recomendable: 2014, versión de Zopfli embebida desconocida (posiblemente anterior a fixes de 2015–2016 que cambian la salida), target .NET Framework, DLL nativa propia. Además el fork fonttools/zopfli podría diferir de google/zopfli.
- Licencia: Zopfli Apache-2.0 → incluir NOTICE/LICENSE en la distribución.

### Gaps
- No pude comparar el commit fonttools/zopfli 400a507 con google/zopfli master (API GitHub bloqueada); desconozco si el fork tiene cambios que alteren la salida.
- No verificado empíricamente que una DLL MSVC produzca bytes idénticos al wheel de Python (que en Windows se compila con MSVC; en Linux con gcc).
- Versión de Zopfli en libzopfli-sharp no documentada.

## 2. Embeber DLL nativa en publicación single-file .NET 8 (WPF)

### Takeaway
Con `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract=true` la DLL nativa va dentro del .exe y se extrae en runtime a disco (por defecto en el directorio temporal del usuario, configurable con `DOTNET_BUNDLE_EXTRACT_BASE_DIR`). Native AOT no es opción para WPF. Alternativa controlada: recurso embebido + `NativeLibrary.SetDllImportResolver` / `NativeLibrary.Load`, o portar a C# y evitar nativo.

### Cited Findings
- `IncludeNativeLibrariesForSelfExtract=true` incrusta las librerías nativas en el único archivo y las extrae en runtime; introducido en .NET 5, desactivado por defecto — [MS Learn single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) (vía resultado de búsqueda; página no accesible directamente); [Lightrun/dotnet sdk issue](https://lightrun.com/answers/dotnet-sdk-request-set-includenativelibrariesforselfextract-default-to-true)
- Si `DOTNET_BUNDLE_EXTRACT_BASE_DIR` está definido, la extracción va a un subdirectorio de esa ruta — [MS Learn single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- `IncludeAllContentForSelfExtract` extrae todos los archivos antes de ejecutar (comportamiento del single-file de .NET Core 3.x) — [MS Learn single-file overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- Problemas reportados con librerías cargadas por `LibraryLoader` propio que no encuentran la librería extraída (caso MongoDB libmongocrypt) — [MongoDB JIRA CSHARP-3844](https://jira.mongodb.org/browse/CSHARP-3844)
- Plan de .NET: zlib nativo (`System.IO.Compression.Native`) también es nativo y se ve afectado por single-file — [dotnet/runtime #101465](https://github.com/dotnet/runtime/issues/101465)

### Inferences
- En Windows la ubicación por defecto de extracción es `%TEMP%\.net\<app>\<hash>\` **[NO VERIFICADO — de memoria, no pude abrir MS Learn]**. La extracción se cachea por hash del bundle (no en cada ejecución) **[NO VERIFICADO]**. Implicaciones: (a) algunos AV/EDR marcan .exe que escriben y cargan DLLs desde %TEMP%; (b) políticas AppLocker/WDAC que bloquean ejecución en %TEMP% rompen la carga; (c) limpiezas de %TEMP% fuerzan re-extracción.
- WPF en sí trae DLLs nativas (`wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, `D3DCompiler_47_cor3.dll`, etc.); en .NET 8 un WPF self-contained single-file **ya necesita** `IncludeNativeLibrariesForSelfExtract=true` para ser un solo .exe, así que añadir `zopfli.dll` no cambia el perfil de riesgo **[NO VERIFICADO — conocimiento previo]**.
- Native AOT no soporta WPF (WinForms/WPF dependen de COM/reflexión) **[NO VERIFICADO — conocimiento previo; aplica igual a .NET 10]**. Por tanto el "static linking" de C en el exe vía `DirectPInvoke`/`NativeLibrary` items de AOT no está disponible.
- Patrón alternativo determinista: incluir `zopfli.dll` como `EmbeddedResource`, al arrancar escribirlo en `%LOCALAPPDATA%\<App>\native\<sha256>\zopfli.dll` (verificando hash) y registrar `NativeLibrary.SetDllImportResolver(typeof(X).Assembly, (name,asm,path) => name=="zopfli" ? NativeLibrary.Load(rutaExtraida) : IntPtr.Zero)`. Más control de ruta y verificación de integridad que la extracción automática.
- Firmar con Authenticode tanto el .exe como la DLL reduce falsos positivos de AV.
- Opción sin nativo: port C# (ver §1 opción B) — elimina extracción y AV por completo; recomendado si los golden tests pasan.

### Gaps
- No pude leer la página de MS Learn para citar textualmente la ruta por defecto en Windows ni la nota específica de WPF.

## 3. ¿Deflate de System.IO.Compression (.NET 8) es byte-idéntico a zlib -9 de Python?

### Takeaway
No hay garantía: .NET 8 en Windows usa una variante optimizada (zlib-intel / Chromium zlib) y .NET 9+ usa zlib-ng, cuyo output difiere explícitamente de madler/zlib. Para bytes idénticos a Python hay que usar la misma implementación de zlib que el intérprete Python que generó los golden files (P/Invoke a madler zlib de la misma versión o port managed fiel).

### Cited Findings
- Para .NET 9 el equipo planeó reemplazar "madler/zlib y zlib-intel" por zlib-ng y aceptó el cambio de salida como churn normal de versión mayor; objetivo: el tamaño con compresión óptima no crezca >10% — [dotnet/runtime #101465](https://github.com/dotnet/runtime/issues/101465)
- Mantenedor de zlib-ng: usa algoritmos de deflate, hashing, matching y configuración distintos a zlib, "la salida probablemente no será la misma" — [zlib-ng discussion #905](https://github-redirect.dependabot.com/zlib-ng/zlib-ng/discussions/905)
- .NET ajusta windowBits=8→9 para raw deflate/gzip para imitar zlib clásico con zlib-ng — [source.dot.net CompressionFormat / ZLibNative](https://source.dot.net/System.IO.Compression/System/IO/Compression/CompressionFormat.cs.html)
- En plataformas móviles .NET enlaza zlib clásico, no zlib-ng — [dotnet/runtime PR #127081 (espejo)](https://gh.hhj520.top/dotnet/runtime/pull/127081)
- `ZLibDotNet`: port managed que soporta formatos zlib y raw deflate (no gzip/zip) — [NuGet ZLibDotNet](https://www.nuget.org/packages/ZLibDotNet); [GitHub mgnsm/ZLibDotNet](https://github.com/mgnsm/ZLibDotNet)

### Inferences
- En .NET 8, `CompressionLevel.SmallestSize` → nivel 9 y `Optimal` → nivel 6; `DeflateStream` = raw deflate, `ZLibStream` = zlib **[NO VERIFICADO — conocimiento previo]**. Aun con nivel 9, la implementación nativa de .NET 8 en Windows x64 es zlib-intel (no madler) → la salida puede diferir de Python.
- Python: el módulo `zlib` de CPython en Linux usa la zlib del sistema; en Windows los binarios oficiales traen su propia zlib, y CPython 3.14 en Windows pasó a zlib-ng **[NO VERIFICADO]**. ⇒ el golden file depende de qué Python/OS lo generó. Registrar `zlib.ZLIB_VERSION` / `zlib.ZLIB_RUNTIME_VERSION` del entorno Python que generó los golden.
- Estrategia: (1) P/Invoke a una `zlib1.dll` madler compilada de la misma versión (p.ej. 1.3.1) con `deflateInit2(level=9, Z_DEFLATED, wbits=-15, memLevel=8, Z_DEFAULT_STRATEGY)` — mismos parámetros que `zlib.compressobj(9, DEFLATED, -15)`; o (2) un port managed fiel de madler zlib (ZLibDotNet declara ser port de zlib; verificar versión y fidelidad byte-a-byte). (3) Como red de seguridad, cambiar el criterio de test: comparar `inflate(salida) == entrada` + `len(salida) <= hueco`, en lugar de igualdad de bytes, para los parches sencillos.
- Si el parche requiere igualdad exacta porque el firmware verifica un hash/CRC del bloque comprimido, hay que fijar implementación; si solo verifica el contenido descomprimido, basta la validación por inflate + tamaño.

### Gaps
- No verifiqué qué versión de zlib exacta usa ZLibDotNet ni si su salida coincide con madler zlib.
- No encontré nota oficial de breaking change de .NET 9 sobre bytes comprimidos (solo el issue).

## 4. ImageSharp (Six Labors): capacidades JPEG y licencia

### Takeaway
ImageSharp NO permite escribir tablas de cuantización personalizadas ni leerlas públicamente; `RestartInterval` existe pero (en main) solo se aplica en codificación progresiva. No sirve para reproducir el comportamiento de Pillow (`qtables=im.quantization`, `restart_marker_blocks=171`, baseline). Además requiere clave de licencia en build desde 3.2.0/4.0.0.

### Cited Findings
- `JpegEncoder` (rama main) expone solo: `Quality` (int?, 1–100), `Progressive`, `ProgressiveScans` (2–64, def. 4), `RestartInterval` (0–65535, def. 0, "currently supported only in progressive encoding"), `Interleaved` (bool?), `ColorType` (JpegColorType?) — sin propiedad de tablas de cuantización — [GitHub JpegEncoder.cs](https://github.com/SixLabors/ImageSharp/blob/main/src/ImageSharp/Formats/Jpeg/JpegEncoder.cs)
- `JpegMetadata` público: `Quality` (máximo de calidad luma/croma estimada), `ColorType`, `Progressive`; `LuminanceQuality`/`ChrominanceQuality` son `internal`; no expone tablas — [GitHub JpegMetadata.cs](https://github.com/SixLabors/ImageSharp/blob/main/src/ImageSharp/Formats/Jpeg/JpegMetadata.cs)
- Issue #1705: imágenes con tablas inusuales se re-codifican mal; propuesta de guardar tablas decodificadas en metadata como fallback (no implementado como API pública según el código actual) — [ImageSharp #1705](https://github.com/SixLabors/ImageSharp/issues/1705)
- Versiones NuGet: 4.1.3 (9-oct-2026, última estable), 4.1.0 (11-ago-2026), 4.0.0 (12-may-2026), 3.2.0 (9-oct-2026), 3.1.12 (29-oct-2025). NuGet marca 4.0.0–4.1.2 y 3.1.x con vulnerabilidades de severidad alta; no 4.1.3 ni 3.2.0. Target net8.0 (compatibles net9/net10) — [NuGet SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp)
- Six Labors Split License v1.0: Apache-2.0 si (a) se usa en software con licencia Open Source o Source Available, (b) es dependencia transitiva, (c) dependencia directa de empresa/individuo con ánimo de lucro con **< 1M USD de ingresos brutos anuales**, o (d) ONG/caridad registrada. En otro caso: Six Labors Commercial License (sixlabors.com/pricing) — [LICENSE ImageSharp main](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE); [texto en 3.1.3](https://packages.nuget.org/packages/SixLabors.ImageSharp/3.1.3/License)
- Desde 3.2.0 (rama anterior) y 4.0.0 (rama actual) los proyectos que referencian directamente ImageSharp requieren **clave de licencia válida en build**; Six Labors lo presenta como aplicación de los términos existentes, no cambio de licencia; solo afecta a dependencias directas; las claves "community" para OSS requieren declarar el `AssemblyName` de cada proyecto que referencia el paquete (incl. tests) — [docs.sixlabors.com (vía resultado de búsqueda)](https://docs.sixlabors.com/articles/imagesharp/); [NuGet SixLabors.Licensing](https://www.nuget.org/packages/SixLabors.Licensing)
- Confusión reportada sobre el umbral de ingresos y "uso comercial" — [fossed](https://dariusz-wozniak.github.io/fossed/library/imagesharp)

### Inferences
- ImageSharp descartado para este caso: no hay tablas personalizadas, DRI solo progresivo, y su DCT/cuantización/cabeceras difieren de libjpeg-turbo → imposible igualar bytes de Pillow.
- Si se quisiera seguir con ImageSharp sin clave, la última versión sin enforcement sería 3.1.x, pero 3.1.x está marcada vulnerable en NuGet → no recomendable.

### Gaps
- No pude leer docs.sixlabors.com para confirmar el procedimiento exacto y coste de la clave community vs comercial en 2026.
- No confirmé si 4.x añadió API de tablas personalizadas fuera de JpegEncoder (el código de main no la muestra).

## 5. Alternativas JPEG: jpegtran/libjpeg-turbo, LibJpeg.NET, WIC, JpegLibrary; y estrategia para asegurar tamaño y baseline

### Takeaway
Rotación 180° sin pérdidas de un JPEG 1366 px de ancho no es "perfecta": 1366 no es múltiplo de 8 (MCU 8x8 en 4:4:4), la columna parcial derecha (6 px) no se puede transformar; `-trim` la descarta (ancho 1360) y `-perfect` falla. Para reproducir el flujo Pillow (decodificar → rotar píxeles → recodificar con las mismas qtables, 4:4:4, baseline, DRI 171) lo más fiel es usar **libjpeg-turbo nativo (misma versión que el Pillow usado)** vía su API libjpeg (no TurboJPEG) o **BitMiracle.LibJpeg.NET** (port managed de libjpeg, con `Restart_interval` y API de tablas estilo libjpeg), validando con golden files.

### Cited Findings
- jpegtran: por defecto conserva silenciosamente los bloques de borde no transformables; `-trim` los descarta (no reversible); `-perfect` hace fallar si la transformación no es perfecta; patrón sugerido `jpegtran -rot 90 -perfect foo.jpg || djpeg foo.jpg | pnmflip -r90 | cjpeg` — [jpegtran(1) Ubuntu](https://manpages.ubuntu.com/manpages/jammy/en/man1/jpegtran.1.html); [jpegtran.1 libjpeg-turbo 3.0.4](https://fuchsia.googlesource.com/third_party/libjpeg-turbo/+/refs/tags/3.0.4/jpegtran.1)
- Ejemplo del manual: "-rot 270 -trim trims only the bottom edge, but -rot 90 -trim followed by -rot 180 -trim trims both edges" — [jpegtran(1) Ubuntu](https://manpages.ubuntu.com/manpages/jammy/en/man1/jpegtran.1.html)
- **BitMiracle.LibJpeg.NET**: "managed version of original libjpeg", licencia tipo BSD (Bit Miracle 2008–2023, basada en IJG); última versión NuGet 1.5.324 (8-may-2020); targets netstandard2.0 / net40 / net46; el repo contiene una carpeta `JpegTran` — [GitHub BitMiracle/libjpeg.net](https://github.com/BitMiracle/libjpeg.net); [NuGet BitMiracle.LibJpeg.NET](https://www.nuget.org/packages/BitMiracle.LibJpeg.NET)
- LibJpeg.NET emite marcadores de reinicio cuando `Restart_interval` o `Restart_in_rows` ≠ 0 — [LibJpeg.NET KB: compression parameter selection](https://bitmiracle.github.io/libjpeg.net/help/articles/KB/compression-parameter-selection.html)
- **JpegLibrary** (yigolden): decodificación, codificación y optimización (reescribir un baseline con Huffman optimizado, ejemplo "JpegOptimize"); MIT; no menciona transformaciones sin pérdida, tablas personalizadas ni DRI — [GitHub yigolden/JpegLibrary](https://github.com/yigolden/JpegLibrary)
- Otras librerías (no .NET) que sí exponen restart interval y tablas: crate Rust `jpeg-encoder` (`set_restart_interval`, `set_quantization_tables`) — [docs.rs jpeg-encoder](https://docs.rs/jpeg-encoder/0.6.1/src/jpeg_encoder/encoder.rs.html); SDK comercial VintaSoft (`JpegEncoderSettings.QuantizationTables`) — [VintaSoft docs](https://www.vintasoft.com/docs/vsimaging-dotnet/Vintasoft.Imaging/Vintasoft.Imaging.Codecs.Encoders.JpegEncoderSettings/QuantizationTables.html)

### Inferences
- **Geometría**: 1366/8 = 170,75 → 171 MCUs por fila; 768/8 = 96 filas exactas. DRI=171 ⇒ un marcador RST por fila de MCUs (96 intervalos, RST0..RST7 cíclicos). Rotación 180: el borde inferior es exacto, el derecho tiene 6 px válidos + 2 de relleno por bloque. Tras rotar 180°, esos 6 px tendrían que quedar en x=0..5 pero el bloque ocupa x=0..7 → desalineado 2 px → imposible sin recodificar. `jpegtran -rotate 180` (sin flags) deja la columna de borde sin transformar (artefacto visible en el borde izquierdo de la imagen rotada... en realidad queda en su posición original — verificar visualmente) **[comportamiento exacto NO VERIFICADO empíricamente]**; `-trim` → 1360x768 (cambia dimensiones del logo: probablemente inaceptable para el firmware); `-perfect` → error.
- jpegtran conserva por defecto las tablas de cuantización y (con `-copy`) marcadores; `-restart N` permite fijar DRI; sin `-optimize`/`-progressive` mantiene Huffman estándar y baseline **[NO VERIFICADO — conocimiento previo del manual]**. Pero por lo anterior no da 1366 px perfectos.
- **Reproducir Pillow byte a byte**: Pillow `rotate(180)` es exacto en píxeles; `save(qtables=..., subsampling=0, progressive=False, restart_marker_blocks=171)` llama a libjpeg-turbo con `jpeg_add_quant_table` (scale 100, force_baseline), `restart_interval=171`, DCT islow por defecto, Huffman estándar (sin `optimize`), cabecera JFIF de Pillow. Para igualdad exacta en .NET hay que: (1) decodificar con el mismo libjpeg-turbo (IDCT islow, 4:4:4 → sin upsampling) — o mejor, comparar a partir del mismo bitmap; (2) recodificar con misma versión de libjpeg-turbo y mismos parámetros; (3) escribir los mismos marcadores APP0/JFIF (densidad, versión). Pillow publica la versión de libjpeg-turbo en `PIL.features.version("jpg")` → fijarla en la herramienta **[recomendado; versión exacta del Pillow del prototipo desconocida]**.
- La API TurboJPEG (tj3*) permite `TJPARAM_RESTARTBLOCKS`, `TJPARAM_SUBSAMP=TJSAMP_444`, calidad, pero **no** cargar tablas de cuantización arbitrarias → hay que usar la API libjpeg clásica (`jpeg_create_compress`, `jpeg_set_defaults`, `jpeg_add_quant_table(…, 100, TRUE)`, `restart_interval=171`) exportada por `jpeg62.dll`/`turbojpeg.dll` **[NO VERIFICADO para libjpeg-turbo 3.1]**. Envolver esto en una DLL C pequeña propia (p.ej. `logo_codec.dll` con una función `encode_rgb_with_qtables(...)`) es más simple que P/Invoke directo de structs libjpeg (layout grande, `setjmp`).
- **LibJpeg.NET** (port de IJG libjpeg 6b): mismo FDCT islow entero, mismas tablas Huffman estándar y misma conversión RGB→YCbCr que libjpeg-turbo (que es compatible bit a bit con su ruta C islow) ⇒ salida probablemente idéntica en datos de escaneo; cabeceras JFIF pueden diferir (versión 1.01 vs 1.02, densidad). Es managed (sin extracción nativa), pero no se actualiza desde 2020. Debe tener `jpeg_add_quant_table` y `Restart_interval` en su API clásica (`jpeg_compress_struct`) **[jpeg_add_quant_table NO VERIFICADO; Restart_interval verificado]**. Es la opción managed más prometedora — validar con golden file.
- **WIC / System.Windows.Media.Imaging.JpegBitmapEncoder** (WPF): expone `QualityLevel`, `Rotation`, `FlipHorizontal/Vertical`; no permite tablas personalizadas ni DRI ni elegir subsampling de forma fiable (WIC tiene `ChromaSubsampling` vía propiedades del encoder nativo, no en el wrapper WPF) **[NO VERIFICADO]** → descartado. System.Drawing (GDI+) solo `Encoder.Quality` → descartado.
- **Garantizar tamaño máximo y baseline** (estrategia recomendada):
  1. Recodificar con las qtables originales (DQT copiada), 4:4:4, `progressive=False`, `restart_interval=171`, Huffman estándar → mismo comportamiento que Pillow.
  2. Comprobar `len(jpeg) <= max_bytes`. Si no cabe: (a) eliminar APPn/COM/EXIF innecesarios (JFIF mínimo); (b) activar `optimize_coding` (Huffman óptimo; sigue siendo SOF0 baseline, ~5–10 % menos) — **pero verificar que el decodificador MStar acepte DHT no estándar**; (c) como último recurso, escalar las qtables (perderá la identidad con el original) o reducir contenido.
  3. Validación post-encode: parsear marcadores y exigir SOF0 (0xFFC0) y ausencia de SOF2; DQT idénticas byte a byte a las originales; DRI=171; SOF con 1366x768, 3 componentes con factores 1x1; número de RSTn coherente (96 intervalos); EOI final; y decodificar para comprobar integridad.
  4. Golden test: comparar SHA-256 contra el JPEG generado por el script Python; si difiere solo en cabeceras JFIF, normalizar las cabeceras.
- Alternativa sin re-encode que preserva todo salvo el borde: rotación de coeficientes DCT en C# (invertir orden de bloques y negar coeficientes con índice impar en u o v) reutilizando DQT/DHT/DRI y re-entropía; sigue teniendo el problema de la columna parcial, así que no evita el ajuste de 2 px.

### Gaps
- No pude abrir el manual de jpegtran completo (man.archlinux.org bloqueado) para citar `-restart`, `-copy` y la conservación de DQT.
- No verifiqué la versión de libjpeg-turbo incluida en el Pillow del prototipo ni si TurboJPEG 3.1 añadió soporte de tablas personalizadas.
- No verifiqué empíricamente que LibJpeg.NET genere bytes idénticos a libjpeg-turbo/Pillow.
- No hay información del decodificador JPEG del SoC MStar (si acepta Huffman optimizado o requiere tablas estándar / DRI concreto).

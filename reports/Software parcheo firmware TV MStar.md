# Cómo construir un parcheador MStar fiable y offline

Un programa de escritorio que modifique dumps SPI de 4 MB de placas MStar "no Android" (V56/V59, CPU AEON R2) ocupa un hueco que **ninguna herramienta pública cubre hoy**. Las herramientas abiertas trabajan con `MstarUpgrade.bin` de Android/eMMC o con monitores. Los programadores (RT809H, CH341A) solo leen y escriben la flash. Ninguna hace el ciclo completo: descomprimir la app, parchear panel, IR, mirror o logo, recomprimir y regenerar los CRC. Para hacerlo bien, la aplicación necesita seis piezas:

- **Un motor de formato** que reproduzca exactamente el empaquetador del SDK Chakra3 de MStar: chunk header en 0x20000 con CRC32 no reflejado en +0x14, 0x3800 bytes sin comprimir más deflate crudo con la longitud original como u32 LE al final, tabla TEL con un CRC16 por recurso, y CRC16 y CRC32 globales.
- **Codificadores fijados a una versión**: Zopfli compilado del mismo código C que usa el prototipo Python, y libjpeg (LibJpeg.NET o libjpeg-turbo) para el logo. ImageSharp no sirve y `System.IO.Compression` no garantiza bytes idénticos.
- **Perfiles JSON con verificación previa y posterior**: SHA-256 de la entrada, bytes originales en cada parche, zonas permitidas, y un diff completo que compruebe que solo cambiaron los bytes previstos.
- **Escritura atómica** que nunca toque el archivo original.
- **Una batería de pruebas** con golden files, firmwares sintéticos, pruebas de propiedades, fuzzing y comparación del desensamblador AEON contra el módulo Ghidra de eb4x.
- **Empaquetado** como un único .exe WPF autocontenido sobre **.NET 10**, no .NET 8: el soporte de .NET 8 terminó el 10-nov-2026.

Lo que más limita el proyecto no es el código sino lo que falta saber: qué comprueba realmente el SBOOT de estas placas, el layout binario exacto de `PanelType` en TSUMV59, y si el decodificador JPEG del SoC acepta tablas Huffman optimizadas. Todo eso hay que validarlo con dumps reales antes de distribuir la herramienta.

## Ninguna herramienta pública hace el ciclo completo sobre SPI de 4 MB

Lo que existe se divide en tres grupos, y ninguno resuelve el caso. Los **desempaquetadores abiertos** son el primero. `dipcore/mstar-bin-tool` (unas 351 estrellas) trabaja con `MstarUpgrade.bin`: asume una cabecera de 16 KB con un script de MBOOT y compresión LZO, no deflate ni chunk headers con CRC ([unpack.py](https://raw.githubusercontent.com/dipcore/mstar-bin-tool/master/unpack.py)). El más cercano es `shinyquagsire23/mstar_extract_decompress`, para firmware de monitores. Localiza la tabla `0x54454C09` ("TEL\t"), lista sus entradas y descomprime en deflate crudo las marcadas con el flag 4, pero **no recomprime ni corrige el CRC** ([main_extract.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/main_extract.c)). `linux-chenxing/mstarfwcutter` se basa en registros `MXPT` de SoCs nuevos y no sirve para placas con tabla TEL ([mstarfwcutter.py](https://raw.githubusercontent.com/linux-chenxing/mstarfwcutter/master/mstarfwcutter.py)).

El segundo grupo son las **herramientas comerciales**. La más parecida en funciones es MstarTool v3.3.0: logos, perfiles de panel y mirror, mando a distancia y licencia por hardware. Pero trabaja con particiones eMMC/EXT4 de Smart TV y no menciona SPI, deflate ni CRC ([README MstarTool](https://raw.githubusercontent.com/YoelCode-ui/MstarTool/HEAD/README.md)). El tercer grupo son los **programadores**: RT809H por ISP VGA/HDMI ([manual iFix](https://www.manualslib.com/manual/1499705/Ifix-Rt809-Series.html)), flashrom `mstarddc_spi`, que los mantenedores deshabilitaron por defecto por dudas de calidad ([flashrom ML](https://www.flashrom.org/pipermail/flashrom/2015-February/013359.html)), y un script de 2026 que lee la flash dos veces y compara los MD5 ([mstar-monitor-firmware-dumper](https://raw.githubusercontent.com/memues/mstar-monitor-firmware-dumper/HEAD/README.md)). Todos copian bytes; ninguno edita el contenido.

Los foros como Elektroda muestran que la práctica habitual es **buscar un dump ajeno que encaje con la combinación placa + panel + mirror y flashearlo** ([topic3859739](https://www.elektroda.com/rtvforum/topic3859739.html), [topic4174233](https://www.elektroda.com/rtvforum/topic4174233.html)). Eso sobrescribe las claves HDCP y los ajustes de fábrica guardados en los bancos del final de la flash ([MApp_FlashMap.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/common/app/include/MApp_FlashMap.h)). La propuesta de valor del programa, por tanto, no es solo comodidad: es **editar el dump propio de cada placa** en lugar de reemplazarlo. Su conjunto mínimo de funciones lo dicta el formato: parsear la TEL, descomprimir y recomprimir la app, editar el panel, el logo y el IR, recalcular todos los CRC, y guardar diff y copia de seguridad. Los ejecutables chinos del tipo "V59 logo tool" que aparecen en páginas SEO **no se pudieron verificar** y no deben tomarse como referencia ([ejemplo SEO](https://edx.ipeindia.org/atlas-lyric/glade-nest/mstar-bin-tool-guiv232-download-link)).

## El SDK Chakra3 filtrado explica cada offset del dump

La mejor fuente primaria es el SDK "Chakra3" de MStar filtrado en GitHub (snapshot de 2017), el middleware "no OS" de estas placas. Sus BSP `*_nos_r2m` corresponden a AEON R2 y contiene código específico para V56. Sus constantes **cuadran exactamente con los offsets del proyecto**: SBOOT de 64 KB, código PM de 64 KB, chunk header de 0x80 bytes y `UNCOMPRESSED_LEN = 0x3800`. Por tanto la app empieza en 0x20080 y el deflate en 0x20080 + 0x3800 = **0x23880** ([BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py)). El Makefile comprime con `mscompress -c -u 14336 -9` (deflate) o con `mscompress7` (LZMA) ([Makefile](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/Makefile)).

### Cuatro sumas de control que el motor debe regenerar

El script de empaquetado define cuatro integridades. Primero, el **CRC32 de la app**: polinomio 0x04C11DB7, init 0, sin reflejar, sin xorout. Cubre desde 0x20080 la parte sin comprimir más el deflate, y se escribe en little-endian en `0x20000+0x14`; en `+0x1C` va `file_size`. Segundo, un **CRC16 por recurso de la TEL**: polinomio 0x8005, init 0, en big-endian, tras rellenar con ceros hasta que `(len+2) % 8 == 0`. Tercero, un **CRC16 global** desde 0x10000 hasta el final menos 2 bytes. Cuarto, un **CRC32 global** que el script añade al final del archivo ([BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py)). En el lado del firmware, `DoMsDecompression` lee la **longitud original como u32 LE en los últimos 4 bytes** del stream y rechaza la imagen si no coincide ([ms_decompress.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/ms_decompress.c)).

La consecuencia de diseño es importante. **La TEL (0x22080) está dentro de la parte sin comprimir que cubre el CRC32**, así que mover o redimensionar un recurso obliga a recalcular ese CRC. Recomprimir con otro compresor (zlib o Zopfli) debería ser aceptable si se cumplen cuatro condiciones: mantener el trailer de longitud, actualizar el "fin en flash", que el resultado quepa antes de `min(B_FAddr)` y recalcular el CRC. Esto es **inferencia [NO VERIFICADO contra un SBOOT real]**. Tampoco consta si el SBOOT de TSUMV59/V56 comprueba los CRC16 globales o los de cada recurso; el SDK solo dice que la entrada final 0xF000 de la TEL "will be verified by bootloader" ([InfoBlock.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/dvbt/InfoBlock.c)). Lo prudente es **recalcular siempre todo lo que exista** y exigir, antes de cada parche, una prueba de ida y vuelta: verificar todos los CRC del dump original con el algoritmo propio.

### La TEL, el logo y la cabecera MS_IMG_INFO

La TEL empieza con `0x54454C09` + `"X3i\0"`, seguidos de un u32 BE con el tamaño de flash reservado y un u16 BE con el número de entradas. Cada entrada ocupa 11 bytes, en big-endian: `B_ID` u16, `B_FAddr` u32, `B_Len` u32 y `B_IsComp` u8. Los flags de compresión son 0x01 LZSS, 0x04 deflate y 0x08 LZMA ([InfoBlock.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/dvbt/InfoBlock.c), [BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py)). En la BSP milan, el **logo de arranque es un JPEG** con `BIN_ID_JPEG_BOOT_LOGO_DVB 0x0300`, y existe un `BIN_ID_PANEL 0x0747` (Panel.Bin, unos 2 KB). Los números de ID **pueden variar según el chip** ([BinInfo.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/bininfo/milan/BinInfo.h)). La estructura `MS_IMG_INFO` contiene `u8PanelType` en +0x46 y en +0x47 un magic de compresión: **0xDEADBE para deflate y 0xDEADEF para LZMA**. La herramienta debe leer ese magic y negarse a continuar si encuentra una variante que no implementa.

### Panel e IR: datos tipados sí, pero con layout por chip

El struct `PanelType` es público a través de cabeceras filtradas (por ejemplo `TI_8BIT_MODE=2` y `OUTPUT_8BIT_MODE=2`). Sin embargo, **el ancho de algunos campos cambia entre BSP**: `m_ucPanelHSyncBackPorch` es U8 en edison y U16 en maya_nos_r2m ([apiPNL.h edison](https://raw.githubusercontent.com/Benjamin-Dobell/kogan-tv-gpl/master/MBoot/MstarCore/bsp/edison/include/apiPNL.h), [apiPNL.h maya](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/core/drv_bsp/maya_nos_r2m/include/apiPNL.h)). Por eso el perfil debe declarar el layout de forma explícita (offset, tipo y endianness de cada campo) y validar valores plausibles: Width 1366/1920, HTotal > Width. Nunca debe suponerlo.

En el IR, el código de cliente NEC (`IR_HEADER_CODE0/1`) se escribe en los registros `IR_CCODE_L/H` desde código AEON ([msIR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/device/ir/maya/msIR.c)). Las teclas se traducen con un `switch` compilado ([MApp_IR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_IR.c)), así que remapear el mando suele implicar **parchear instrucciones, no una tabla**. Hay una excepción: si el dump incluye el recurso "IR Bin" (`ENABLE_IR_BIN`), basta editar datos y recalcular una suma de 32 bits y el CRC16. Si esa suma falla, el firmware solo avisa y continúa ([MApp_IR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_IR.c)). Para el mirror hay dos posibilidades: un flag compilado (`MirrorEnable`) o un valor en los bancos de base de datos. **No está verificado cuál aplica a las placas del proyecto**.

## La igualdad de bytes obliga a fijar Zopfli y libjpeg

La salida de C# solo será byte a byte idéntica a la del prototipo Python si se usan **las mismas implementaciones de compresión**.

En **deflate**, `System.IO.Compression` no sirve como referencia. En .NET 8 para Windows usa una variante zlib-intel, y desde .NET 9 usa zlib-ng, cuyo mantenedor reconoce que "la salida probablemente no será la misma" que la de zlib clásico ([dotnet/runtime #101465](https://github.com/dotnet/runtime/issues/101465), [zlib-ng #905](https://github-redirect.dependabot.com/zlib-ng/zlib-ng/discussions/905)). Zopfli, en cambio, es **determinista**: su PRNG usa una semilla fija (`m_w=1; m_z=2`). Además, py-zopfli 0.4.3 solo emite zlib o gzip, y quitarle los 2 bytes de cabecera y los 4 de Adler-32 deja exactamente la salida de `ZopfliCompress` con `ZOPFLI_FORMAT_DEFLATE`. Así lo muestra el código del sdist, que fija el fork fonttools/zopfli en el commit `400a507` ([PyPI zopfli](https://pypi.org/project/zopfli/), [fonttools/py-zopfli](https://github.com/fonttools/py-zopfli)).

El único wrapper NuGet, libzopfli-sharp, es de 2014 y no se sabe qué versión de Zopfli embebe ([NuGet](https://www.nuget.org/packages/libzopfli-sharp)), así que no se recomienda. Quedan dos opciones. La primera es compilar esos mismos `.c` como `zopfli.dll` x64 sin `/fp:fast`. La segunda es portar unas 3.000–4.000 líneas a C# para no depender de código nativo. En los dos casos **la igualdad de bytes con el wheel de Python está [NO VERIFICADA]**, porque los costes se calculan con `double` y `log()`. Debe demostrarse con golden files. Conviene recordar también que el bootloader solo verifica el contenido descomprimido y el CRC de lo que se escribe. La igualdad exacta con Python es **un requisito de reproducibilidad y de pruebas, no de arranque**. El criterio mínimo aceptable es `inflate(salida) == entrada` y `len(salida) <= hueco disponible`.

En **JPEG**, ImageSharp queda descartado por dos motivos. Técnicamente, no permite fijar tablas de cuantización y su `RestartInterval` "currently supported only in progressive encoding" ([JpegEncoder.cs](https://github.com/SixLabors/ImageSharp/blob/main/src/ImageSharp/Formats/Jpeg/JpegEncoder.cs)). Además, desde 3.2.0/4.0.0 exige una clave de licencia al compilar, y la licencia comercial se aplica a empresas con ingresos de 1 M USD o más ([LICENSE](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE)). WIC y GDI+ tampoco permiten fijar tablas ni DRI **[NO VERIFICADO]**.

Para reproducir el flujo de Pillow (tablas originales, 4:4:4, baseline, `restart_marker_blocks=171`) quedan dos opciones. Una es **BitMiracle.LibJpeg.NET**, un port gestionado de libjpeg con licencia tipo BSD que admite `Restart_interval` ([KB LibJpeg.NET](https://bitmiracle.github.io/libjpeg.net/help/articles/KB/compression-parameter-selection.html)), aunque no se actualiza desde 2020 ([NuGet](https://www.nuget.org/packages/BitMiracle.LibJpeg.NET)). La otra es una pequeña DLL en C sobre la API libjpeg clásica de libjpeg-turbo, porque TurboJPEG no carga tablas arbitrarias **[NO VERIFICADO para 3.1]**.

La geometría impone un límite que conviene conocer. Como **1366 no es múltiplo de 8**, cada fila tiene 171 MCU de 8×8 (de ahí DRI = 171: un marcador de reinicio por fila) y la última columna solo tiene 6 píxeles válidos. Una rotación de 180° sin pérdida con jpegtran es imposible: `-perfect` falla y `-trim` deja la imagen en 1360 px ([jpegtran(1)](https://manpages.ubuntu.com/manpages/jammy/en/man1/jpegtran.1.html)). Hay que decodificar y recodificar. Tras codificar, la herramienta debe comprobar:

- que el marcador de inicio de frame es SOF0 (baseline) y no hay SOF2 (progresivo);
- que las tablas DQT son idénticas a las originales y que DRI vale 171;
- que la imagen es 1366×768 con 3 componentes 1×1;
- que hay 96 intervalos de reinicio y el archivo termina en EOI;
- que el resultado ocupa como mucho `maxSize`.

Si no cabe, los pasos son, en este orden: quitar los segmentos APPn, COM y EXIF; activar Huffman optimizado, **solo si se confirma que el decodificador MStar lo acepta (desconocido)**; y, como último recurso, escalar las tablas de cuantización.

## Perfiles: hash de entrada, bytes originales y diff de postcondición

Los formatos de parche maduros enseñan una cosa clara. BPS y UPS llevan un CRC del origen, del destino y del propio parche ([spec BPS](https://gist.github.com/anonymous/bd56fc12003b3fde03f122d681828040), [crate ups](https://docs.rs/ups/latest/ups/struct.UpsPatch.html)), mientras que IPS no verifica nada y es el contraejemplo clásico ([Data Crystal](https://datacrystal.romhacking.net/wiki/Patch)). RomRaider, por su parte, separa la identificación de la imagen (`internalidaddress`/`internalidstring`) de la definición de tablas ([RomRaider XML Guide](https://mailserver.romraider.com/index.php/RomRaider/EditorXMLReferenceGuide)). El perfil propuesto combina ambas ideas:

- un bloque `target` con SHA-256, tamaño (4.194.304) y comprobaciones de ID que permitan mensajes claros del tipo "esta es la versión X, no la Y";
- `patches[]` con `address`, `original` y `replacement` en hex. Tener siempre los bytes originales permite verificar la precondición, detectar si el parche ya estaba aplicado y revertirlo;
- `options` que asocian cada opción de la interfaz con un conjunto de parches;
- `tables` y `logoSlots` con dominio y tamaño máximo;
- `expectedOutputSha256` siempre que el número de combinaciones sea finito.

Los checksums se declaran en el perfil (algoritmo, rango, ubicación), pero **se calculan en código y nunca con fórmulas aportadas por el perfil**. Las direcciones van como cadenas hex, no como números JSON. El `baseoffset` decimal de los XDF de TunerPro muestra lo fácil que es equivocarse ([HP Tuners](https://docs.hptuners.com/VCM_Editor_User_Defined_Parameters/01_UserDefinedParameters/)).

La carga del perfil se defiende en capas:

1. Límite de tamaño (por ejemplo, 1 MB).
2. Hash o firma calculados sobre los bytes crudos, antes de parsear.
3. `JsonDocument` con profundidad máxima limitada y sin comentarios, más una pasada propia que rechace claves duplicadas. La opción `AllowDuplicateProperties` llegó en .NET 10 **[NO VERIFICADO]**.
4. Esquema JSON 2020-12 validado con JsonSchema.Net ([docs](https://docs.json-everything.net/schema/basics)).
5. Deserialización estricta con `JsonUnmappedMemberHandling.Disallow` ([MS Learn](https://learn.microsoft.com/en-gb/dotnet/api/system.text.json.serialization.jsonunmappedmemberhandling?view=net-8.0)), source generator y sin polimorfismo `$type`.
6. Validación semántica en código: rangos dentro de la imagen, sin solapes, longitud de `original` igual a la de `replacement`.

Newtonsoft.Json.Schema queda excluido: su nivel gratuito es AGPL y está limitado a 1000 validaciones por hora ([newtonsoft.com/jsonschema](https://www.newtonsoft.com/jsonschema)).

La integridad tiene dos niveles. Los perfiles oficiales van **embebidos como recurso, con una lista blanca de SHA-256 compilada**. Los perfiles externos llevan una **firma separada ECDSA P-256 sobre los bytes exactos del archivo**, lo que evita tener que canonicalizar el JSON. Ed25519 no está en la BCL, ni siquiera en .NET 10 ([dotnet/runtime #14741](https://github.com/dotnet/runtime/issues/14741)). Los perfiles sin firma solo se cargan en un modo desarrollador explícito, con aviso y marcados como tales en el informe. La firma no sustituye a las zonas.

El motor aplica los cambios sobre una **copia en memoria** y, al terminar, recorre los 4 MB comprobando que el conjunto de bytes cambiados está dentro de los rangos planificados y que cada rango contiene exactamente lo esperado. En 4 MB eso cuesta milisegundos con `Span<byte>`. Hay **zonas que nunca se escriben**: SBOOT (0x0–0xFFFF) y PM (0x10000–0x1FFFF), cuyo daño exige un programador externo, y los bancos HDCP, de fábrica y de ajustes del final de la flash.

La escritura sigue un orden fijo:

1. Archivo temporal en el mismo directorio, abierto con `FileOptions.WriteThrough`.
2. `Flush(true)`.
3. Releer el temporal y comprobar su hash.
4. Renombrar con `MoveFileExW(REPLACE_EXISTING | WRITE_THROUGH)`. La documentación no promete atomicidad, pero es la práctica de natefinch/atomic y de Terraform ([MoveFileEx](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexa), [antonymale.co.uk](https://antonymale.co.uk/windows-atomic-file-writes.html)). Se reintenta con espera creciente si un antivirus bloquea el archivo.

La salida nunca puede tener la misma ruta que la entrada. Cada ejecución genera además un informe JSON determinista con versión de la herramienta, hash del perfil, hashes de entrada y salida, operaciones con los bytes antiguos y nuevos, y el resultado de la verificación.

## Pruebas: golden files, firmware sintético, propiedades y fuzzing

La fiabilidad se demuestra con capas de pruebas que no requieren distribuir firmware con copyright:

- **Golden files**: para cada perfil oficial y combinación de opciones, snapshots del plan de operaciones y del informe con Verify, más el `outputSha256` esperado ([VerifyTests/Verify](https://github.com/VerifyTests/Verify)). También golden files del deflate y del JPEG generados por el prototipo Python, registrando la versión de zlib y de libjpeg-turbo de ese entorno.
- **Firmware sintético**: imágenes de 4 MB generadas con PRNG y semilla fija, con los bytes `original` insertados, para ejecutar todo el pipeline sin dumps reales.
- **Pruebas de propiedades** (CsCheck/FsCheck **[NO VERIFICADO]**): cambios siempre dentro de las zonas, idempotencia, `revertir(aplicar(x)) == x` y rechazo de solapes.
- **Fuzzing** del cargador de perfiles con SharpFuzz/libFuzzer, que ya ha encontrado CVEs en el propio .NET ([sharpfuzz](https://github.com/metalnem/sharpfuzz)). La única excepción permitida es `ProfileValidationException`.

Los firmwares reales quedan fuera de git. Un manifiesto con SHA-256 y una variable de entorno indican dónde están, y las pruebas se omiten si faltan.

Para el ensamblador y desensamblador AEON portado de Python, la ISA debe ser **una sola tabla de datos (JSON) compartida** por las dos implementaciones. Encima se apilan cinco pruebas: vectores golden, ida y vuelta exhaustivo (2^16 y 2^24 casos son barribles), fuzzing diferencial Python contra C#, comparación contra Ghidra con el módulo de eb4x y barrido lineal de una imagen completa. Es la misma metodología con la que eb4x obtuvo "zero unexplained differences" frente al objdump del fabricante ([eb4x README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md)).

## AEON R2 tiene referencia comunitaria, no manual oficial

No existe manual público de la ISA BA2/AEON. Beyond Semiconductor y CAST solo publican folletos, que describen instrucciones de 16, 24, 32 y 48 bits ([CAST BA21](https://www.cast-inc.com/pdfs/cast_ba21-brief.pdf)). La referencia más útil es **`eb4x/ghidra-plugin-aeon`** (Apache-2.0, noviembre de 2026), generado a partir de las tablas de `aeon-elf-as` del fabricante y contrastado con su desensamblador. Según este módulo, en AEON R2 las instrucciones ocupan **2, 3 o 4 bytes, siempre en big-endian** aunque los datos sean little-endian, y la longitud se deduce de los 3 bits altos del primer byte. Pueden **empezar en cualquier byte** y **no hay instrucciones de 48 bits**. No hay delay slots; r1 es el puntero de pila, r9 la dirección de retorno, los argumentos van en r3–r8 y r10–r22 se preservan entre llamadas ([eb4x](https://github.com/eb4x/ghidra-plugin-aeon)).

Dos consecuencias prácticas. Si el prototipo implementa instrucciones de 48 bits, probablemente mezcla la BA2 genérica (Jennic/CAST) con AEON R2, y hay que revisarlo. Y un parche debe ocupar exactamente los bytes que reemplaza (o rellenar con NOP de la longitud correcta) y no caer en medio de una instrucción, comprobándolo con un barrido desde un límite conocido. No se encontró soporte AEON en binutils, Capstone, Rizin ni IDA. El toolchain `aeon-elf`/`r2-elf` que circula es software propietario filtrado: **no debe redistribuirse ni copiarse literalmente en tablas**. Lo correcto es seguir el patrón de eb4x, donde reensamblar con el toolchain del fabricante es algo "local only" y la integración continua solo comprueba los bytes ya publicados en el repositorio.

## Empaquetar en .NET 10 y asumir que el exe irá sin firma

**.NET 8 dejó de recibir soporte el 10-nov-2026**, y .NET 10 LTS lo tiene hasta el 14-nov-2028 ([política de soporte](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)). Hay una pega: la lista oficial de sistemas de .NET 10 **no incluye Windows 10 22H2** Home/Pro, solo las ediciones LTSC/IoT, porque ese Windows salió de soporte en octubre de 2025 ([supported-os.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)). Como 22H2 comparte base con LTSC 2021, casi seguro que funciona, pero hay que probarlo en una máquina virtual.

Para obtener un solo .exe se combinan `PublishSingleFile`, `SelfContained`, `win-x64` e `IncludeNativeLibrariesForSelfExtract`. Las DLL nativas de WPF, y `zopfli.dll` si se usa, **se extraen a `%TEMP%\.net`** sin necesidad de permisos de administrador ([single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)). Dos limitaciones condicionan el diseño:

- **WPF no admite trimming** ([trimming incompatibilities](https://github.com/dotnet/docs/blob/main/docs/core/deploying/trimming/incompatibilities.md)) y, en consecuencia, tampoco Native AOT. El ejecutable tendrá decenas de MB **[tamaño NO VERIFICADO, medir]**.
- `Assembly.Location` devuelve una cadena vacía en single-file; hay que usar `AppContext.BaseDirectory`.

Los ajustes se guardan en `%LOCALAPPDATA%` y el manifiesto declara `asInvoker`. Un port de Zopfli a C# eliminaría la extracción de código nativo, aunque las DLL de WPF se extraen igualmente.

La firma de código choca con la geografía. Artifact Signing (unos 9,99 USD al mes) solo admite **desarrolladores individuales en EE. UU. o Canadá**. **EV ya no salta SmartScreen desde 2024**, y OV cuesta 150–300 USD al año ([code-signing-options](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/package-and-deploy/code-signing-options.md), [quickstart](https://github.com/MicrosoftDocs/azure-docs/blob/main/articles/artifact-signing/quickstart.md)). Si el desarrollador reside en Cuba, es probable que ninguna CA comercial le emita un certificado **[NO VERIFICADO]**. Lo realista es distribuir sin firma y reducir los falsos positivos de antivirus:

- sin compresión del single-file ni UPX;
- con metadatos e icono completos;
- enviando cada versión al portal WDSI de Microsoft ([casos Wacatac](https://dylanbeattie.net/2024/12/09/dotnet-9-aot-zip-file-windows-defender-false-positives.html)).

Un archivo copiado por USB, sin Mark-of-the-Web, no activa SmartScreen **[NO VERIFICADO]**. GitHub, incluido Actions, está disponible en Cuba, y sirve para compilar en `windows-latest` cuando bajar el SDK y los paquetes NuGet es difícil ([GitHub trade controls](https://github.com/github/docs/blob/main/content/site-policy/other-site-policies/github-and-trade-controls.md)).

Para el visor hexadecimal hay que **evitar WPFHexaEditor**, que ahora es AGPL-3.0 ([NuGet](https://www.nuget.org/packages/WPFHexaEditor)). Las alternativas son HexBox (MIT, WinForms, apto para .NET 10) dentro de `WindowsFormsHost` ([NuGet HexBox](https://www.nuget.org/packages/HexBox)) o un visor propio virtualizado.

## El riesgo legal está en distribuir binarios, no la herramienta

En EE. UU., la exención DMCA §1201 de 2024 cubre diagnóstico, mantenimiento y reparación de dispositivos de consumo, y otra exención trata la interoperabilidad de smart TVs. Ambas se están renovando en el ciclo de 2027 ([Finnegan](https://www.finnegan.com/en/insights/ip-updates/final-rule-issued-in-the-us-copyright-offices-ninth-triennial-section-1201-proceeding.html), [EFF](https://www.copyright.gov/1201/2027/petitions/renewal/Renewal-Pet-Jailbreaking-Smart-TVs-Electronic-Frontier-Foundation.pdf)). Además, §1201 solo entra en juego si se elude una medida técnica, y este firmware no parece ir cifrado ni firmado.

En la UE, la Directiva 2009/24 permite la descompilación para lograr interoperabilidad ([art. 6](https://www.legislation.gov.uk/eudr/2009/24/article/6)), y el art. 5.1 permite la corrección de errores **[no confirmado en texto]**. La Directiva 2024/1799 prohíbe a los fabricantes las trabas de software a la reparación de "electronic displays" ([Comisión Europea](https://commission.europa.eu/law/law-topic/consumer-protection-law/directive-repair-goods_en)). En Cuba rige la Ley 154/2022 ([WIPO Lex](https://www.wipo.int/wipolex/es/legislation/details/21621)), pero **no se pudieron verificar sus excepciones**.

La línea práctica es esta: distribuir la herramienta y perfiles hechos solo de offsets, unos pocos bytes y hashes; **no redistribuir dumps del fabricante ni el toolchain AEON**; e incluir un aviso "tal cual, sin garantía", el riesgo de brick, la recomendación de leer la SPI con un programador antes de escribir y advertencias eléctricas (inversores, tensión del panel). Esto no es asesoría legal.

## Conclusión

El trabajo difícil no está en la interfaz WPF sino en **convertir un formato poco documentado en un contrato verificable**. Cada escritura debe poder justificarse con una precondición (hash y bytes originales), una zona permitida y una postcondición (diff y CRC recalculados). El SDK Chakra3 da casi todas las constantes, pero se escribió para otro chip. La herramienta debe tratarlas como hipótesis que cada perfil confirma contra un dump real, no como verdades fijas en el código. Eso pide perfiles con detección explícita de la variante (deflate frente a LZMA, layout de `PanelType`, IR Bin frente a código) y una negativa clara cuando algo no cuadra.

Hay una segunda idea menos evidente. **La reproducibilidad byte a byte con Python es una herramienta de pruebas, no un requisito del hardware.** Fijar Zopfli y libjpeg permite usar golden files y SHA-256 de salida esperada; pero si esa igualdad no se logra en .NET, la seguridad no depende de ella, sino de la verificación por inflate y decodificación, de los límites de tamaño y de los CRC. Separar ambas cosas evita bloquear el proyecto por diferencias de punto flotante entre compiladores.

## Decisiones recomendadas

| Área | Decisión recomendada | Motivo / evidencia | Estado |
|---|---|---|---|
| Runtime | `net10.0-windows`, `win-x64`, self-contained, single-file, `IncludeNativeLibrariesForSelfExtract=true`, sin compresión, `DebugType=embedded` | .NET 8 sin soporte desde 10-nov-2026; WPF no admite trimming ni AOT | Verificado (política .NET); compatibilidad con Win10 22H2 **[NO VERIFICADO, probar en VM]** |
| UI | WPF + CommunityToolkit.Mvvm; visor hex propio o HexBox (MIT) | WPFHexaEditor es AGPL-3.0 | Licencias verificadas; CommunityToolkit **[NO VERIFICADO versión]** |
| Formato | Motor propio: chunk header 0x20000 (CRC32 0x04C11DB7 sin reflejar en +0x14, `file_size` en +0x1C), app 0x20080, deflate desde 0x23880 con trailer u32 LE, TEL 11 bytes BE, CRC16 0x8005 por recurso, CRC16 y CRC32 globales | Scripts de Chakra3 | Constantes verificadas en SDK; qué valida el SBOOT **[NO VERIFICADO]** |
| Variantes | Detectar magic 0xDEADBE/0xDEADEF y flags TEL; abortar si la variante no está soportada (LZMA, LZSS) | `MS_IMG_INFO`, Makefile | Verificado en SDK |
| Deflate | Zopfli del commit `400a507` (py-zopfli 0.4.3) con `ZOPFLI_FORMAT_DEFLATE`; preferir port C#, alternativa DLL nativa sin `/fp:fast`; nunca `System.IO.Compression` como referencia | Determinismo de Zopfli; zlib-ng difiere | Igualdad con Python **[NO VERIFICADO, golden files]** |
| JPEG | Decodificar → rotar → recodificar con LibJpeg.NET (o DLL sobre libjpeg-turbo): DQT original, 4:4:4, baseline, DRI 171, Huffman estándar; validar los marcadores y `maxSize` | ImageSharp sin tablas ni DRI baseline; 1366 px impide la rotación sin pérdida | Huffman optimizado y tolerancia del decodificador MStar **[NO VERIFICADO]** |
| Perfiles | JSON con `target.sha256`, ID checks, parches con bytes `original`/`replacement`, layouts tipados, `logoSlots`, `expectedOutputSha256`; direcciones como cadenas hex | Precedentes BPS/UPS y RomRaider | Diseño propio |
| Carga de perfiles | Límite de tamaño → hash o firma de los bytes → `JsonDocument` limitado + rechazo de duplicados → JsonSchema.Net 2020-12 → STJ estricto con source generator → validación semántica | Defensa en capas | APIs de .NET 9/10 **[NO VERIFICADO]** |
| Confianza | Perfiles oficiales embebidos + lista blanca SHA-256; externos con firma ECDSA P-256 separada; modo desarrollador para los no firmados | Ed25519 no está en la BCL | Verificado (issue dotnet/runtime) |
| Zonas | Prohibir 0x0–0x1FFFF y los bancos DB/HDCP/fábrica; diff completo de postcondición (`changed ⊆ planned`) | Daño irreversible sin programador; pérdida de HDCP | Diseño propio |
| E/S | Nunca sobrescribir la entrada; backup por hash; temporal + WriteThrough + relectura + `MoveFileExW(REPLACE_EXISTING\|WRITE_THROUGH)` | Práctica de natefinch/atomic y Terraform | Atomicidad no garantizada por la documentación |
| Pruebas | xUnit + Verify; firmware sintético; CsCheck/FsCheck; SharpFuzz; fixtures reales fuera de git con manifiesto SHA-256 | Evita distribuir copyright | — |
| ISA AEON | Una sola tabla JSON compartida Python/C#; ida y vuelta exhaustivo; comparación diferencial contra eb4x/Ghidra; instrucciones de 2/3/4 bytes, big-endian, sin delay slots | Módulo eb4x verificado contra el objdump del fabricante | Ausencia de 48 bits según eb4x (fuente comunitaria) |
| Firma y AV | Distribuir sin firma (salvo socio en país elegible), sin compresión ni UPX, metadatos completos, envío a WDSI y comprobación en VirusTotal | Artifact Signing solo EE. UU./Canadá para individuos; EV ya no salta SmartScreen | Disponibilidad de CA para Cuba **[NO VERIFICADO]** |
| Build | `global.json` + `Directory.Packages.props` fijados; feed NuGet offline o GitHub Actions `windows-latest` | GitHub disponible en Cuba | Cuotas de Actions **[NO VERIFICADO]** |
| Legal | Distribuir solo la herramienta y perfiles mínimos; no incluir dumps ni el toolchain AEON; aviso de responsabilidad y seguridad | DMCA, Directiva 2009/24, Ley 154 | Excepciones cubanas **[NO VERIFICADO]** |

## Preguntas abiertas y riesgos

1. **Qué valida el SBOOT de TSUMV59/V56/MSD3393.** El CRC32 del chunk casi seguro; el CRC16 y el CRC32 globales, los CRC16 por recurso y la entrada 0xF000, sin confirmar. Hay que probar en placa con un dump de recuperación a mano.
2. **Rango exacto del CRC32 de la app.** No está claro si va hasta el "fin en flash" o hasta `file_size` en +0x1C; hay que confirmarlo con una prueba de ida y vuelta sobre dumps reales.
3. **Recompresión de otro tamaño.** Que el bootloader acepte un deflate de longitud distinta (actualizando "fin en flash" y el trailer) es una inferencia; hay que verificar que no invade `min(B_FAddr)`.
4. **Layout binario de `PanelType` en TSUMV59.** Varía por BSP (U8 frente a U16 y bitfields del compilador AEON) y no hay layout verificado para este chip.
5. **Mirror.** Puede ser un flag compilado, un valor en los bancos DB o el recurso `Panel.Bin` (0x0747); el formato de `Panel.Bin` es desconocido.
6. **IR.** Falta saber si los dumps del proyecto incluyen IR Bin o solo código, y los valores de `IRBIN_KEYMAP_CODE`/`IRBIN_BIN_END` no se encontraron.
7. **Decodificador JPEG del SoC.** Se desconoce si acepta Huffman optimizado, otros DRI o tamaños distintos de 1366×768, y cuál es el tamaño máximo real del hueco del logo.
8. **Igualdad de bytes de Zopfli y JPEG en .NET frente a Python** (punto flotante, versión de libjpeg-turbo del Pillow del prototipo): **[NO VERIFICADO]**.
9. **Instrucciones de 48 bits en el prototipo**: posible mezcla de BA2 genérica con AEON R2.
10. **Windows 10 22H2 fuera de la lista oficial de .NET 10**: riesgo formal de que Microsoft no atienda fallos específicos de esa versión.
11. **Extracción a `%TEMP%`**: puede romperse con AppLocker/WDAC o ser marcada por EDR en equipos de taller.
12. **Falsos positivos de antivirus** en un exe sin firma que manipula binarios: el riesgo es alto y recurrente.
13. **Licencias**: ni dipcore ni shinyquagsire23 declaran licencia (no reutilizar su código); `ms_decompress.c` es "GPL-2.0-only OR BSD-3-Clause"; Zopfli es Apache-2.0 y hay que incluir NOTICE.
14. **Legalidad en Cuba** según la Ley 154/2022 y en el país de cada usuario: no verificada; los perfiles no deben contener fragmentos amplios de código del fabricante.
15. **Calidad del ISP por VGA/DDC** (RT809H, flashrom): fuera del alcance del programa, pero una escritura parcial puede inutilizar la placa. El programa debe exigir una lectura doble con hash antes de aceptar un dump como entrada.

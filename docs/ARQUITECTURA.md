# Arquitectura y seguridad

## Proyectos

```
FirmReaper.App (WPF, net10.0-windows) ─┐
FirmReaper.Cli (consola, net10.0) ─────┼──> FirmReaper.Core (net10.0, sin dependencias externas)
FirmReaper.Core.Tests (xUnit v3) ──────┘
```

El motor (`Core`) no depende de Windows: se prueba en Linux y en Windows con el mismo código.
Única dependencia externa: `BitMiracle.LibJpeg.NET` (gestionada, BSD) para decodificar JPEG (solo la parte
entrópica, la IDCT y la salida YCbCr; el ensanchado de la crominancia y el color se hacen en `LogoImage`).

## Componentes de `FirmReaper.Core`

| Archivo | Responsabilidad |
|---|---|
| `Crc32Msb.cs` | CRC-32 MSB de MStar |
| `Crc16Tel.cs` | CRC-16 (0x8005) del final de cada recurso TEL |
| `Compression/RawInflater.cs` | Descompresor deflate propio (dice dónde termina el flujo) |
| `MStar/MStarLayout.cs` | Constantes del formato |
| `MStar/AppHeader.cs` | Cabecera en 0x20000 |
| `MStar/TelTable.cs` | Tabla de recursos |
| `MStar/ImgInfo.cs` | Bloque MS_IMG_INFO |
| `Analysis/FirmwareAnalyzer.cs` | Análisis completo → lista de comprobaciones |
| `Catalog/catalog.json` | Imágenes conocidas (incrustado en el .dll) |
| `Catalog/FirmwareCatalog.cs` | Carga estricta y validada del catálogo |
| `Catalog/Identification.cs` | Original / nuestra / referencia / no reconocido |
| `Reporting/AnalysisReport.cs` | Informe en texto y JSON |
| `Fmt.cs` | Números en formato español fijo |
| `Compression/Zopfli/*.cs` | Port de Zopfli (idéntico a py-zopfli 0.4.3) |
| `Compression/ZlibDeflater.cs` | Port de zlib 1.3 (nivel 4–9), idéntico a Python `zlib.compressobj(9, …, -15, 8)` |
| `Compression/Zopfli/ExactLog.cs` | Logaritmo determinista = glibc (evita diferencias Windows/Linux) |
| `Profiles/Profile.cs`, `ProfileLoader.cs` | Formato JSON de perfiles y su validación |
| `Profiles/ProfileRegistry.cs` | Perfiles integrados + carpeta externa `perfiles\` |
| `Profiles/Builtin/*.json` | Perfiles integrados (LG, Konka, ATEC, placa LM2SA ×2) |
| `Patching/FirmwareBuilder.cs` | Aplica parches, recomprime, recalcula cabecera/CRC y verifica |
| `Patching/OutputWriter.cs` | Escritura atómica, sin sobrescribir, con copia de seguridad |
| `Reporting/BuildReport.cs` | Informe de cada firmware generado |
| `Imaging/JpegInfo.cs` | Lectura validada de la estructura de un JPEG (sin decodificar) |
| `Imaging/JpegBaselineEncoder.cs` | Codificador JPEG que reproduce byte a byte a libjpeg-turbo (configuración de Pillow) |
| `Imaging/LogoImage.cs` | Formato del logo (`LogoFormat`); decodificar como libjpeg-turbo; girar; encajar una imagen en el tamaño del logo; codificar ajustando la calidad al hueco |
| `Imaging/ImageDecoder.cs` | Lectura validada de la imagen del usuario: JPEG, PNG (propio, con `RawInflater`) y BMP 24/32 bits |
| `Profiles/KeyTable.cs` | Tabla botón → función del mando (formatos lista8, lista4, directa256): codificar, decodificar, validar, importar/exportar JSON; código de cliente NEC (`NecCustomer`, `ClientCodeSpec`, codificación de `bg.sfeqi`) |

Interfaz (`FirmReaper.App`): `MainWindow` (analizar, generar, cambios en hexadecimal), `KeyMapWindow`
(editor de la tabla del mando).

### ¿Por qué un descompresor propio?
`DeflateStream` de .NET no informa del byte en que termina el flujo; ese dato es necesario para
comprobar la cola y el "fin en flash" (en Python es `unused_data`). Además nos permite limitar el
tamaño de salida. Está probado contra `DeflateStream` y con datos aleatorios.

### ¿Por qué .NET 10 y no .NET 8?
.NET 8 terminó su soporte el 10-nov-2026; .NET 10 es LTS hasta nov-2028. Nota: Microsoft no lista
Windows 10 22H2 como soportado por .NET 10 (sí las ediciones LTSC); se espera que funcione, **hay que
comprobarlo** en un Windows 10 real.

## Seguridad (revisión de la etapa 1)

| Riesgo | Medida |
|---|---|
| Modificar o dañar el firmware | La etapa 1 **solo lee**: `FileAccess.Read` + `FileShare.Read`. Una prueba comprueba que el análisis no altera el array |
| Archivo enorme o no válido | Se comprueba el tamaño **antes** de leer (solo 4 MB exactos) |
| Offsets maliciosos en el archivo | Cada offset de cabecera/TEL se valida contra el tamaño antes de usarse; aritmética en `ulong`/`long` para evitar desbordes |
| "Bomba" de descompresión | Salida limitada a 32 MB; el descompresor solo lanza `InvalidDataException` (probado con 2 000 entradas aleatorias) |
| Catálogo manipulado | Va **incrustado** en el ensamblado; carga estricta (campos desconocidos, duplicados, huellas mal formadas, orígenes inexistentes → error) |
| Sobrescribir un firmware con el informe | El diálogo de guardado rechaza `.bin` y el archivo analizado |
| Permisos | Manifiesto `asInvoker`: nunca pide administrador. Sin acceso a red |
| Firmwares del fabricante en git | `.gitignore` excluye `firmware_local/` y `*.bin` |

## Seguridad (revisión de la etapa 2: escritura)

| Riesgo | Medida |
|---|---|
| Grabar un firmware equivocado | Huella SHA-256 del original obligatoria; base de la app comprobada; bytes originales de cada parche comprobados |
| Perfil dañado o manipulado | JSON estricto; cada parche dentro de una zona permitida; sin solapes entre parches combinables; límites de tamaño (perfil 512 KB, parche 4 KB, zona 64 KB, 256 parches) |
| Perfil externo que suplanta a uno integrado | Rechazado si usa un `id` integrado; el informe indica si es externo y su SHA-256 |
| Escribir fuera de lo previsto | Tras generar se analiza la salida y se compara byte a byte: solo cambian los bytes de los parches; fuera de la app, idéntico |
| Que no quepa | Se comprueba contra el primer recurso TEL antes de escribir |
| Compresión no reversible | Se descomprime y compara antes de escribir |
| Sobrescribir archivos | Nunca: `FileMode.CreateNew` + renombrado sin sobrescribir; si el nombre existe → `_2`, `_3`… |
| Archivo a medio escribir | Temporal en la misma carpeta → volcado a disco → relectura y comparación → renombrado |
| Perder el original | Copia de solo lectura en `respaldo_originales\` (se comprueba su huella; si ya existe con otro contenido, se detiene) |
| Original cambiado entre análisis y guardado | Se vuelve a calcular su huella antes de guardar |
| Resultado distinto en Windows | `ExactLog` hace la compresión independiente de la biblioteca matemática del sistema |

| Logo propio manipulado | Parser de marcadores con límites; baseline, dimensiones exactas, tamaño máximo, sin basura tras FFD9, decodificación completa, máximo 4096×4096 píxeles y 4 MB de archivo (ampliado en la etapa 6, ver abajo) |
| Tabla del mando manipulada | Solo sustituye los bytes nuevos de un parche ya autorizado (misma dirección y longitud); botones y funciones de listas cerradas; sin repetidos; máximo de entradas; archivo JSON estricto y del mismo perfil |
| Escribir fuera del hueco del logo | La verificación final solo admite cambios en el hueco del recurso TEL del logo (JPEG + ceros); el resto del recurso y de la flash, idénticos |

### ¿Por qué un codificador JPEG propio?
Para que el logo girado sea **idéntico** al probado en el TV. LibJpeg.NET **codifica distinto** que
libjpeg-turbo (que cuantiza con recíprocos). El codificador propio replica el camino de libjpeg-turbo
(4:4:4, 4:2:2 y 4:2:0: `jccolor`, `jcsample`, `jfdctint`, `jcdctmgr`, `jchuff`, bloques ficticios de `jccoefct`)
y da los mismos bytes que Pillow, sin bibliotecas nativas.

### ¿Y la decodificación? (corregido en la etapa 6)
En la etapa 3 se escribió que LibJpeg.NET "decodifica igual que Pillow". **Solo es cierto en 4:4:4** (el Konka).
LibJpeg.NET sigue el modelo de libjpeg 7/8: con "fancy upsampling" calcula la crominancia con una IDCT escalada
(16x8 o 16x16), mientras que libjpeg-turbo (API 6b, la de Pillow) la decodifica a 8x8 y la ensancha con un filtro
triangular. Con el logo en color del LG (4:2:2) salían hasta 17 niveles de diferencia en el 0,5 % de los bytes. (El
ATEC, 4:2:0, coincidía por casualidad: su logo es blanco y negro y la crominancia es plana.)
Solución: LibJpeg.NET entrega YCbCr **sin** "fancy" (crominancia a 8x8, solo duplicada → se recupera exacta) y
`LogoImage` aplica el filtro de `jdsample.c` y la conversión de `jdcolor.c`. Ahora Konka, ATEC y LG girados son
byte a byte los de Pillow.

## Seguridad (revisión de la etapa 6: logo en ATEC y LG, imágenes del usuario)

| Riesgo | Medida |
|---|---|
| Imagen del usuario manipulada (JPEG/PNG/BMP) | Todo se trata como dato no confiable: máximo 64 MB de archivo y 36 millones de píxeles (≤ 16384 por lado); PNG con CRC de cada bloque y Adler-32, cabecera zlib comprobada, descompresión limitada al tamaño exacto de la imagen (sin "bombas"), filtros y paletas con límites, entrelazado rechazado; BMP solo 24/32 bits sin comprimir y con límites calculados en 64 bits; JPEG: estructura validada antes de decodificar. Cualquier incoherencia → `InvalidDataException` → no se genera nada. Pruebas con 2 000 PNG/BMP y 3 000 JPEG corrompidos |
| Perfil externo que intente escribir en otra zona con `zonaFlash` | Al cargar: la zona debe estar a partir de `0x23080` y dentro de 4 MB. Al generar: debe empezar después de la app, de todos los recursos TEL y del fin del área TEL (`0x22088`), y en el original tiene que haber **un JPEG de exactamente** `longitudJpeg` bytes y `ancho`×`alto` seguido **solo** del relleno. Una zona con datos (ajustes, canales…) no cumple eso, así que se rechaza sin tocar nada |
| Recurso TEL con CRC-16 | Con `crc16` se exige que el CRC original sea correcto; se recalcula y se vuelve a comprobar en la salida |
| Logo nuevo que el TV no sepa mostrar | Siempre se escribe en el formato exacto del original (dimensiones, submuestreo, tablas, reinicios); un JPEG del usuario solo se usa tal cual si ya tiene ese formato. Validación final: SOF0, en color, dimensiones y submuestreo, sin datos tras FFD9, cabe y se decodifica |
| Escribir fuera del hueco | Verificación final: idéntico al original fuera del hueco (y de los 2 bytes del CRC); dentro, el JPEG y después solo el relleno |
| Consumo excesivo | Como máximo 15 intentos de codificación para ajustar la calidad; el redimensionado guarda solo las filas que aún necesita |

## Seguridad (revisión de la etapa 8: mando genérico)

| Riesgo | Medida |
|---|---|
| Código de cliente mal escrito | Solo se acepta exactamente 2 bytes hexadecimales; en el ATEC se rechaza `b2 > 7F` (el inmediato con signo nunca coincidiría y el TV dejaría de obedecer a cualquier mando de ese tipo) |
| Parches en el código de arranque sin comprimir (1.3.0) | Solo en flash `0x23080`–`0x23880`, después de la tabla TEL (comprobado al cargar el perfil y al generar); bytes originales comprobados en la flash; CRC-32 de la cabecera recalculado; verificación final: idéntico al original fuera de esos bytes; MS_IMG_INFO y TEL quedan intactos |
| Resolución del panel (1.3.0) | Solo tiempos publicados (hoja de datos, CEA-861, VESA DMT); márgenes por regla fija documentada; un canal LVDS (no se activa el doble canal); cada parche dentro de la entrada #1; la del LP141WX1 comprobada igual a la de `patch_panel.py` |
| Perfiles de la placa LM2SA (1.4.0) | Generados por la misma función que el ATEC, que se niega a escribir el perfil si la huella, la entrada #1 (1366x768, nombre `WXGA_AU20_T200XW02`), las instrucciones del mando/reposo o el logo no son los esperados; zonas permitidas = las mismas 5 que el ATEC en sus direcciones; recompresión Zopfli verificada; el perfil del ATEC sale idéntico |
| Código de cliente cambiado solo en parte (lo que pasó en el ATEC de la 1.1.0: encendido sí, reposo no) | Los campos de reposo se declaran en el perfil y se escriben siempre junto al principal; al cargar, una opción que toque el cliente debe cambiar TODOS sus campos al mismo código |
| Escribir fuera del campo del cliente | El perfil fija dirección y formato; al cargar se exige que esté en una zona permitida y que todo parche que lo toque sea EXACTAMENTE ese campo y parta del original declarado; al generar se comprueban los bytes originales y que solo cambió lo previsto |
| Tabla editada que desborde | Tamaño fijo por formato; en las listas, máximo `entradas − 1` teclas (Konka: 45, porque detrás hay otros datos); funciones solo de la lista cerrada del perfil (`0xFF` reservada); sin botones repetidos |
| Tabla en su sitio | Sus bytes originales van en el perfil y se comprueban en el firmware; debe estar en una zona permitida y ningún parche de ninguna opción puede tocarla; si no se edita, no se escribe |
| Códigos de botón arbitrarios (otro mando) | Son solo un dato de la tabla (0x00–0xFF); en `lista8` se rechaza el que coincide con el terminador |
| Archivo de tabla importado | JSON estricto, ≤ 256 KB, ≤ 256 asignaciones, del mismo perfil y validado igual que en el editor |
| Confundir un archivo personalizado con uno probado | No se compara con las versiones documentadas; el informe dice "ELEGIDO"/"EDITADA" y el nombre lleva `_cliente-XXXX` / `_tabla-editada` |

Perfiles firmados: no se implementan (uso por una sola persona); se registra la huella de cada perfil.

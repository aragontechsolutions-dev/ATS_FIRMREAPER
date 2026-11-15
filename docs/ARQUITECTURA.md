# Arquitectura y seguridad

## Proyectos

```
FirmReaper.App (WPF, net10.0-windows) ─┐
FirmReaper.Cli (consola, net10.0) ─────┼──> FirmReaper.Core (net10.0, sin dependencias externas)
FirmReaper.Core.Tests (xUnit v3) ──────┘
```

El motor (`Core`) no depende de Windows: se prueba en Linux y en Windows con el mismo código.
Única dependencia externa: `BitMiracle.LibJpeg.NET` (gestionada, BSD) para decodificar el logo.

## Componentes de `FirmReaper.Core`

| Archivo | Responsabilidad |
|---|---|
| `Crc32Msb.cs` | CRC-32 MSB de MStar |
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
| `Compression/Zopfli/ExactLog.cs` | Logaritmo determinista = glibc (evita diferencias Windows/Linux) |
| `Profiles/Profile.cs`, `ProfileLoader.cs` | Formato JSON de perfiles y su validación |
| `Profiles/ProfileRegistry.cs` | Perfiles integrados + carpeta externa `perfiles\` |
| `Profiles/Builtin/*.json` | Perfiles integrados (LG, Konka) |
| `Patching/FirmwareBuilder.cs` | Aplica parches, recomprime, recalcula cabecera/CRC y verifica |
| `Patching/OutputWriter.cs` | Escritura atómica, sin sobrescribir, con copia de seguridad |
| `Reporting/BuildReport.cs` | Informe de cada firmware generado |
| `Imaging/JpegInfo.cs` | Lectura validada de la estructura de un JPEG (sin decodificar) |
| `Imaging/JpegBaselineEncoder.cs` | Codificador JPEG que reproduce byte a byte a libjpeg-turbo (configuración de Pillow) |
| `Imaging/LogoImage.cs` | Decodificar (LibJpeg.NET) y girar 180° el logo |

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

| Logo propio manipulado | Parser de marcadores con límites; baseline, dimensiones exactas, tamaño máximo, sin basura tras FFD9, decodificación completa, máximo 4096×4096 píxeles y 4 MB de archivo |
| Escribir fuera del hueco del logo | La verificación final solo admite cambios en el hueco del recurso TEL del logo (JPEG + ceros); el resto del recurso y de la flash, idénticos |

### ¿Por qué un codificador JPEG propio?
Para que el logo girado sea **idéntico** al probado en el TV. LibJpeg.NET (port de libjpeg 6b) decodifica
igual que Pillow pero **codifica distinto** (libjpeg-turbo cuantiza con recíprocos). El codificador propio
replica el camino de libjpeg-turbo y da los mismos bytes, sin bibliotecas nativas (antes se valoró incluir
libjpeg-turbo nativo; ya no hace falta).

Perfiles firmados: no se implementan (uso por una sola persona); se registra la huella de cada perfil.

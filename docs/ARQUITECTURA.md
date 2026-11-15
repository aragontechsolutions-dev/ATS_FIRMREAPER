# Arquitectura y seguridad

## Proyectos

```
FirmReaper.App (WPF, net10.0-windows) ─┐
FirmReaper.Cli (consola, net10.0) ─────┼──> FirmReaper.Core (net10.0, sin dependencias externas)
FirmReaper.Core.Tests (xUnit v3) ──────┘
```

El motor (`Core`) no depende de Windows: se prueba en Linux y en Windows con el mismo código.
No usa paquetes NuGet de terceros (solo la biblioteca estándar de .NET).

## Componentes de `FirmReaper.Core` (etapa 1)

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

Pendiente para etapas con escritura: zonas permitidas por perfil, verificación de bytes originales,
diff final, escritura atómica (temporal + renombrado), copia de seguridad y perfiles externos firmados.

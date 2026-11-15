# ATS_FIRMREAPER — FirmReaper

Programa para Windows 10/11 que **analiza y modifica** firmwares de placas de TV
**MStar** (CPU AEON R2, memoria SPI de 4 MB) mediante **perfiles** de parches verificados.

> **Versión 1.0.0 — las 5 etapas completadas.** Manual de usuario: [docs/MANUAL_USUARIO.md](docs/MANUAL_USUARIO.md)
> (también en [PDF](docs/MANUAL_USUARIO.pdf)). Analiza, identifica y verifica volcados `.BIN`, y genera
> las versiones modificadas definidas en perfiles (hoy: LG 42LF652VX mando Soyea / espejo; Konka TV4-32K16
> espejo en el menú de fábrica o fijo, y logo girado / propio / sin cambios; ATEC 32L18D panel LP141WX1
> 1280x800, 8 o 6 bits).
> Plan completo en [docs/ETAPAS.md](docs/ETAPAS.md).

## Qué hace hoy

### Analizar (no modifica nada)

- Abre un volcado SPI de 4 MB (botón o arrastrar a la ventana).
- **Identifica** el archivo por su huella SHA-256 frente al catálogo interno:
  - original conocido (ATEC 32L18D, LG 42LF652VX, Konka TV4-32K16),
  - versión generada por nosotros (y si se probó en el TV y funcionó),
  - firmware de referencia (Soyea),
  - **no reconocido**: con contenedor válido (modificado por otro / no analizado) o dañado.
- **Verifica el contenedor** (11 comprobaciones): tamaño, cabecera, coherencia RAM, **CRC-32**,
  tabla de recursos TEL, que la app no pise los recursos, descompresión, cola, fin en flash,
  bloque MS_IMG_INFO y zona libre.
- Muestra cabecera, tamaño comprimido/descomprimido, **espacio libre**, ID de placa, SBOOT y recursos TEL
  (detecta el logo JPEG).
- Guarda el informe en texto o JSON.

### Generar (pestaña "Generar")
- Si el archivo es un **original con perfil**, muestra sus opciones (p. ej. LG: mando Soyea sí/no, espejo).
- Aplica los parches, recomprime con **Zopfli** (idéntico a los scripts Python), recalcula cabecera y CRC,
  y **verifica** que solo cambió lo previsto. Si algo falla, no escribe nada.
- Guarda el firmware **sin sobrescribir nunca**, una **copia de solo lectura** del original
  (`respaldo_originales\`) y un **informe** con huellas, cambios y procedimiento de grabación.
- Perfiles nuevos sin recompilar: carpeta `perfiles\` junto al `.exe` ([guía](docs/PERFILES.md)).
- LG: **editor de la tabla del mando** (botón → función; importar/exportar).
- Pestaña **Cambios (hex)**: los bytes modificados de cada parche, original frente a nuevo.

## Uso

### Ventana (Windows)
`FirmReaper.exe` → **Abrir .BIN…** (o arrastrar el archivo). No necesita instalación ni administrador.
Si algo falla, el error se muestra y se guarda en `%LOCALAPPDATA%\FirmReaper\errores.log`.

> El artefacto de GitHub Actions trae **dos** ejecutables: `FirmReaper.exe` (la ventana) y
> `firmreaper-cli.exe` (la línea de comandos, para usar desde cmd/PowerShell).

### Línea de comandos (Windows o Linux)
```bash
firmreaper-cli info ARCHIVO.BIN            # análisis completo
```
```bash
firmreaper-cli info ARCHIVO.BIN --json     # el mismo análisis en JSON
```
```bash
firmreaper-cli verificar A.BIN B.BIN ...   # una línea por archivo
```
```bash
firmreaper-cli catalogo                    # imágenes conocidas
```
```bash
firmreaper-cli perfiles                    # perfiles y sus opciones
```
```bash
firmreaper-cli generar ORIGINAL.BIN --perfil lg-42lf652vx-soyea --opcion espejo=off --salida CARPETA
```
```bash
firmreaper-cli tabla-mando --perfil lg-42lf652vx-soyea --salida tabla.json
```
```bash
firmreaper-cli generar ORIGINAL.BIN --perfil lg-42lf652vx-soyea --tabla-mando tabla.json --salida CARPETA
```
```bash
firmreaper-cli generar TV4-32K16.bin --perfil konka-tv4-32k16-mirror --opcion logo=propio --logo MI_LOGO.jpg --salida CARPETA
```
Código de salida: `0` válido y reconocido · `1` no válido o no reconocido · `2` error de uso.

## Compilar

Requisitos: SDK de **.NET 10** (LTS; .NET 8 dejó de tener soporte el 10-nov-2026).

```bash
dotnet build FirmReaper.sln
```
```bash
dotnet test --project tests/FirmReaper.Core.Tests
```
```bash
dotnet publish src/FirmReaper.App -c Release -r win-x64 -o publish
```

El último comando genera `publish/FirmReaper.exe` (≈135 MB, autocontenido: lleva .NET dentro).
GitHub Actions ([.github/workflows/build.yml](.github/workflows/build.yml)) compila, prueba y publica
el `.exe` en Windows en cada push; se descarga como artefacto desde la pestaña *Actions*.

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/FirmReaper.Core` | Motor: CRC, deflate, Zopfli, formato MStar, catálogo, perfiles, generación, informes |
| `src/FirmReaper.App` | Interfaz WPF |
| `src/FirmReaper.Cli` | Línea de comandos |
| `tests/FirmReaper.Core.Tests` | Pruebas automáticas (xUnit v3) |
| `tools/referencia_python` | Scripts Python originales (referencia para comparar resultados) |
| `tools/aeon` | Ensamblador/desensamblador AEON R2 (Python) y tablas de la ISA |
| `tools/perfiles` | Generador de los perfiles integrados a partir de los scripts |
| `licenses/`, `THIRD_PARTY_NOTICES.md` | Licencias de terceros (Zopfli y tablas AEON: Apache 2.0; LibJpeg.NET: BSD; zlib) |
| `docs/` | Documentación técnica ([índice](docs/ETAPAS.md)) |
| `docs/historial` | Guías de los 3 proyectos hechos con los scripts |
| `research_notes/`, `reports/` | Investigación previa (herramientas, formato, librerías, legal) |
| `firmware_local/` | **No se sube a git.** Aquí van los `.BIN` para las pruebas locales |

## Documentación

- [docs/MANUAL_USUARIO.md](docs/MANUAL_USUARIO.md) / [PDF](docs/MANUAL_USUARIO.pdf) — **manual de usuario**.
- [docs/ETAPAS.md](docs/ETAPAS.md) — plan por etapas y estado.
- [docs/FORMATO_MSTAR.md](docs/FORMATO_MSTAR.md) — formato del firmware (verificado vs. no verificado).
- [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md) — diseño del programa y **seguridad**.
- [docs/PERFILES.md](docs/PERFILES.md) — **cómo crear un perfil** para otro modelo.
- [docs/PRUEBAS.md](docs/PRUEBAS.md) — cómo se prueba y con qué archivos.
- [reports/Software parcheo firmware TV MStar.md](reports/Software%20parcheo%20firmware%20TV%20MStar.md) — investigación.

## Aviso

Modificar el firmware puede dejar el TV sin arrancar. Haz siempre una copia del original (3 lecturas con
el mismo hash) y ten un programador SPI a mano. El inversor CCFL trabaja a ~675 Vrms. Úsalo solo en equipos
propios o con permiso del propietario. No se distribuyen firmwares del fabricante.

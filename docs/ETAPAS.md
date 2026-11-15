# Plan por etapas

Inicio: 2026-11-15. Cada etapa se cierra con: código + pruebas automáticas + revisión de seguridad +
documentación, sin romper lo que ya funciona.

| # | Etapa | Estado |
|---|---|---|
| 1 | Motor de lectura y verificación (sin modificar nada) | ✅ Completada |
| 2 | Zopfli en C#, perfiles, generar idéntico a los scripts, escritura segura | ✅ Completada |
| 3 | Logo del Konka (girar / propio / ninguno), editor de la tabla del mando del LG, visor hexadecimal | Pendiente |
| 4 | Perfil ATEC (panel 1280x800, 6/8 bits) | Pendiente |
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
- CLI: `firmreaper perfiles` y `firmreaper generar`. Ventana: pestaña **Generar** (opciones, progreso, cancelar).
- Perfil **LG** (mando Soyea sí/no × espejo original/desactivado) y perfil **Konka** (MIRROR en menú, sin verificar).
- Herramientas AEON en `tools/aeon` (ensamblador/desensamblador Python de referencia).

**Comprobado**
- El programa genera las 2 versiones del LG v5 **idénticas byte a byte** a las de `patch_ir_lg.py`
  (`17918f8f…`, `3a836aee…`), en ~10 s.
- Zopfli C#: 7 vectores de Python idénticos + la app real del LG.
- `aeon_isa.json` idéntico al de eb4x/ghidra-plugin-aeon@788cf8a; `build_cave_v2.py` reproduce la rutina del LG.
- 101 pruebas superadas.

**Problema encontrado y resuelto: el logaritmo de Windows**
- GitHub Actions mostró que `Math.Log` en Windows da distinto último bit que Linux en algunos enteros →
  la compresión podría salir distinta en Windows.
- Además, glibc (Linux, lo que usó Python) **no redondea correctamente** en 57 enteros de 1..2 100 000.
- Solución: `ExactLog` calcula el valor correctamente redondeado con aritmética doble-doble (solo
  operaciones IEEE básicas, igual en cualquier CPU/SO) y aplica una tabla con esas 57 excepciones de glibc.
  Resultado: igual a glibc bit a bit en todo el rango que usa Zopfli.

**No comprobado / pendiente**
- **Konka:** falta el binario original (`TV4-32K16.bin`, `32c52965…`). El perfil se generó desde el script
  pero no se ha podido probar (estado `sin-verificar`). La variante "espejo fijo" (`TV4-32K16_MIRROR.bin`):
  **no tengo** sus parches (no están en los scripts recibidos).
- El logo girado del Konka y el editor del mando del LG → etapa 3.
- La ventana no se ha abierto en un Windows real desde aquí (solo se compila).

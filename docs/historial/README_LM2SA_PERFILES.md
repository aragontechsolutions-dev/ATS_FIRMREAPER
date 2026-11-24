# Placa LM2SA V1.6 (PA20406-2-36ZG) — análisis y perfiles (2026-11-23)

Dos firmwares del usuario (comparación en `README_COMPARACION_LM2SA.md`). Los dos tienen el ID de placa
`BD_MST061B_10AZU_14014` del ATEC 32L18D, el mismo SBOOT/PM y el mismo programa base; cambian las direcciones.
Direcciones = RAM de la app (la app empieza en RAM `0x240000`; base `0x243800`). Comprobado byte a byte y por
desensamblado (`tools/aeon`).

| Elemento | ATEC 32L18D | 20210531 (A) | 20210614 (B) |
|---|---|---|---|
| Tabla de paneles: entrada #1 `WXGA_AU20_T200XW02` 1366x768 (92 bytes; mismas 5 entradas y mismo contenido) | `0x4E3A78` | `0x4D106C` | `0x4BB06C` |
| Comparación del cliente `bg.sfeqi r7,0x7736` (rutina IR, = 0x2B bytes después de su inicio) | `0x3233D5` | `0x31EAC1` | `0x31E049` |
| Inicialización IR, `bn.ori r4,0x36` / `0x77` (registros `0x3DA4`/`0x3DA5`; +0x577 / +0x582) | `0x32394C` / `0x323957` | `0x31F038` / `0x31F043` | `0x31E5C0` / `0x31E5CB` |
| Entrada en reposo ("=> StandBy"), código de arranque sin comprimir | `0x243115` / `0x243120` | igual | igual |
| Tabla código→función (256 bytes, la carga la rutina IR) | `0x422090` | `0x4131F0` (= ATEC) | `0x412460` (código `56` → `0x71`, en el ATEC `0x72`) |
| Logo: el programa carga siempre el recurso TEL `0x0300` (misma función) | `0x2E350F` | `0x2E1CE8` | `0x2E158B` |
| Logo | 2167x1228, 4:2:0 | 720x480, 4:4:4, 55 079 bytes | igual que A |
| Espejo: "Mirror Enable" en los textos del menú de fábrica (recurso `0x0388`) | sí | sí | sí (mismo recurso que A) |
| Libre tras la app (original) | 5 bytes | 6 bytes | 1 byte |

## Compresión
El original no se reproduce con zlib -9 (sale 5–7 KB más grande y no cabría). Zopfli, 5 iteraciones: ~39 300 bytes
libres en los dos. Los perfiles usan Zopfli 5.

## Perfiles
`lm2sa-v16-20210531` y `lm2sa-v16-20210614` (v1), generados por `tools/perfiles/generar_perfiles.py` con la misma
función que el ATEC (`perfil_familia_061b`), que comprueba antes de escribir cada perfil:
- la huella del original, que la entrada #1 sea 1366x768 / 1560 / 806 / 76 MHz y se llame `WXGA_AU20_T200XW02`;
- los bytes de la comparación del cliente, de la inicialización IR (con su `ori r3,0x3da4/0x3da5` delante) y de la
  entrada en reposo;
- que la tabla de teclas tenga 48 funciones y que el logo sea un JPEG completo.
El perfil del ATEC sale idéntico al de la 1.3.0.

## Comprobaciones
- Con "1280x800 + 8 bits + Soyea": 32 bytes cambiados en cada uno; la entrada del panel queda **igual** que la del
  ATEC probado y el mando igual que en el ATEC (`c0ef06e0`, `508037`, `508078`, `50e037`, `50e078`); recursos,
  SBOOT/PM y base de datos sin tocar (FirmReaper y comprobación independiente en Python).
- Logo girado: solo cambia el recurso `0x0300` (CRC-16 correcto, 720x480).
- Pruebas automáticas: `Lm2saCambiaLoMismoQueElAtecProbado` y `Lm2saLogoGiradoSoloCambiaElRecurso` (×2).

## Sin probar en el TV
Todo. Lo más cercano a lo probado es "1280x800 + 8 bits + Soyea" (mismos cambios que el archivo del ATEC que
funcionó). No se sabe si la placa LM2SA es eléctricamente igual a la del ATEC (conector LVDS, tensión del panel).

## Salida LVDS: "Sin cambios" / 8 bits / 6 bits (petición del usuario, 2026-11-24)
La opción `bits` tiene ahora tres valores y por defecto `original` (la que trae el firmware). La entrada #1 original
del ATEC, A y B ya es de 8 bits (`+4` sin el bit de dither, TiBitMode `2`, OutputFormat `0`): el generador calcula
el valor "8" como los bytes que difieran de eso y aquí no hay ninguno, así que "8" no cambia nada (mismo archivo que
"Sin cambios"; prueba `SalidaLvdsSinCambiosY8BitsDanElMismoArchivo`). Las salidas documentadas del ATEC con `bits = 8`
se registran también con `bits = original` (mismo archivo). Se aplica a los perfiles ATEC (v6, mismo número: los
archivos no cambian) y LM2SA.

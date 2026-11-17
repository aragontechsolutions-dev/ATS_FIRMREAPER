# Espejo (mirror) en ATEC y LG — análisis (2026-11-17)

Objetivo de la etapa 9: espejo de imagen **y menús** en los 3 firmwares. El Konka ya estaba resuelto (opción MIRROR en
el menú de fábrica o espejo fijo; `README_KONKA_MIRROR_MENU.md`). Direcciones = RAM de la app.

## Conclusión

**El ATEC y el LG ya tienen el espejo en su menú de fábrica**, y cubre vídeo y menús. No hace falta modificar el
firmware para activarlo o desactivarlo.

| TV | Opción del menú de fábrica | Ajuste (RAM) | Aplicación |
|---|---|---|---|
| ATEC | "Mirror Enable" (textos del recurso `0x0388`) | `0x5FD0E0 + 0xBC` (0/1) | menú `0x2FBA66` → `0x2C0DF6`; arranque/fuente `0x2C0C73`, `0x2C0CBF` |
| LG | "MIRROR" (textos del recurso `0x0385`, junto a "LVDS MAP" y "Swap Channel") | `0x53ED78` (0/1) | menú `0x30F3F4` → `0x2AFC5A`; arranque: paso de la secuencia `0x2BED0F` → `0x2B3EFD` → `0x2AFC1C` |

Al aplicar el ajuste, los dos firmwares hacen lo mismo:
- vídeo: `0x3B8347` (ATEC) / `0x377CE2` (LG) con 1 → modo de espejo del XC = 3 (horizontal + vertical), con 0 → 0;
- menús: `MApi_GOP_GWIN_SetHMirror` y `SetVMirror` (ATEC `0x3366D1` / `0x33646C`; LG `0x33C749` / `0x33C54D`).

Detalles:
- ATEC: además de `+0xBC`, si los bits `0xC0` de `0x5FD0E0 + 0xC8` están activos el espejo se desactiva (ajuste sin
  identificar). Al restaurar los valores de fábrica (`0x2CAD49`) `+0xBC` se pone a 1 salvo que esté activo un bit de
  configuración (`r12 & 8`): con este volcado, **no sé** qué valor queda.
- ATEC: `XC_INITDATA` se rellena en `0x260B4F` (estructura en `r1+0x260`); `bMirror` (`+0x1B`) se pone a 0 en
  `0x260BA6` (`sb 0x27b(r1),r0`). No se usa: el ajuste del menú manda.
- LG: la opción "Espejo: desactivado" de FirmReaper (parche `0x25E644`, `XC_INITDATA.bMirror = 0`) solo cambia el
  valor **inicial**. Según este análisis, si el menú de fábrica tiene MIRROR = ON, el arranque vuelve a activar el
  espejo de vídeo y menús. Por eso se cambió el texto de la opción (los bytes son los mismos; las versiones
  documentadas siguen saliendo idénticas). **Sin probar en el TV.**

## Cómo entrar al menú de fábrica

**NO SÉ** el código de estos modelos (en el Konka era Menu + 8896). Suele ser una secuencia en el menú de imagen o de
"Fuente"; buscar el modelo de placa (`BD_MST061B_10AZU_14014`, `TP.VST56.PB801`).

## Logo

El logo se dibuja antes de que se aplique el espejo: con el panel montado al revés, usar "Logo de arranque: girado"
(etapa 6).

# ATEC 32L18D — opción de resolución del panel (2026-11-22)

Petición del usuario: la resolución original del ATEC es 1366x768; poder elegir esa y las menores que se pueda.

## Qué se cambia

La entrada #1 de la tabla de paneles (`WXGA_AU20_T200XW02`, RAM `0x4E3A78`, 92 bytes; formato en
`README_ATEC_LP141WX1.md`). Se separaron dos opciones independientes:

| Opción | Bytes de la entrada | Parches |
|---|---|---|
| Resolución | +20…+46 (sincronismos, porches, inicio, ancho/alto, totales máx./típ./mín., DCLK máx./típ./mín.) y +76…+83 (MaxSET/MinSET = K/DCLKmín., K/DCLKmáx., K = 129 424 176) | 2 por resolución |
| Salida LVDS 6 bits | +4 (dither) y +64…+71 (TiBitMode 3, OutputFormat 1) | 2 |

Comprobado: con los tiempos del LP141WX1, las dos opciones dan **exactamente** la entrada de `patch_panel.py` (8 y
6 bits), y las salidas documentadas `ab3d3485…` y `7bd55c39…` salen idénticas.

## Tiempos

Solo resoluciones con tiempos publicados (no se inventan):

| Resolución | Fuente | HSync/HBP | VSync/VBP | HTotal | VTotal | DCLK |
|---|---|---|---|---|---|---|
| 1366x768 | firmware original | 20/40 | 4/34 | 1560 | 806 | 76 |
| 1280x800 | hoja de datos LP141WX1 | 32/80 | 6/15 | 1440 (1368–1480) | 823 (809–848) | 71 (66–75) |
| 1280x720 | CEA-861 720p60 | 40/220 | 5/20 | 1650 | 750 | 74,25 |
| 1024x768 | VESA DMT 1024x768@60 | 136/160 | 6/29 | 1344 | 806 | 65 |
| 800x600 | VESA DMT 800x600@60 | 128/88 | 4/23 | 1056 | 628 | 40 |

HStart = HSync + HBP y VStart = VSync + VBP, como en la entrada original y en la del LP141WX1. Márgenes de los
estándares (no los dan): HTotal −5 %/+5 % con porche delantero ≥ 16, VTotal −2 %/+3 %, DCLK −7 %/+7 %
(parecidos a los de la hoja del LP141WX1: −5 %/+3 %, −2 %/+3 %, −7 %/+6 %).

No se incluyen 1024x600, 800x480 ni 640x480: no hay un estándar único para paneles LVDS de esas resoluciones (o el
reloj queda en el límite del transmisor) y habría que inventar valores.

## Sin probar en el TV

1280x720, 1024x768 y 800x600. Cada panel puede pedir otros tiempos: comprobar con su hoja de datos.

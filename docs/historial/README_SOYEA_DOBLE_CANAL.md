# Soyea (BD_MST061B_10AZU_14014) — panel de doble canal LVDS: análisis y PRUEBA (2026-11-27)

Petición: habilitar el doble canal en la placa Soyea (`MST_EXT_SPI_ISP_20230301_153737.BIN`); el panel nuevo se
alimenta a 12 V. Direcciones = RAM de la app (app en `0x240000`).

## Cómo elige el panel el firmware (comprobado)
- Tabla de 5 paneles (92 bytes cada uno), igual que el ATEC: #0 `FullHD_CMO216_H1L01` 1920x1080 **doble canal**
  (`0x4E9404`), #1 `WXGA_AU20_T200XW02` 1366x768 un canal (`0x4E9460`), #2 SXGA 1280x1024, #3 1440x900,
  #4 1680x1050 (los tres doble canal). Doble canal = bit `0x20` del byte `+4` (#0: `0x62`, #1: `0x02`).
- Lista de punteros a las 5 entradas en `0x4E95D0`. El índice es **`u8ResolutionType`**: `0x2609EE` lo lee de
  `[0x602280 + 0xDC]` (ajustes guardados), lo guarda en `0x4E9400` y llama a `0x329361` / `0x3292CE` con él
  ("----u8ResolutionType %x---", `0x260733`).
- El **menú de fábrica** lo cambia: "Panel Setting → **Panel Type**" (textos del recurso `0x0388`); `0x2FFC3D`
  ajusta `[0x602280 + 0xDC]` entre 0 y 4 (paso 1).

## Prueba
- Perfil externo `docs/historial/pruebas/soyea-prueba-fullhd-doble.json`: la entrada **#1** pasa a tener los valores
  de la #0 (todo salvo el puntero al nombre): 1920x1080, doble canal, 2200x1125 (2060–2650 x 1100–1480),
  150 MHz (120–164), TiBitMode 2 (8 bits). 36 bytes distintos.
- `soyea-prueba-fullhd-doble.BIN`, SHA-256 `22f3850446f268581d79ff5f0826df11407c59bbd6e60f7f22a23400fb2abef7`,
  CRC `0x8625188D`; Zopfli 5 (40 969 bytes libres). Solo cambia la entrada; recursos, SBOOT/PM y base de datos
  idénticos (FirmReaper y comprobación independiente).
- Solo tiene efecto si el TV usa la entrada #1 (Panel Type = 1). Alternativa sin grabar nada: con el firmware
  original, menú de fábrica → Panel Type = 0 (la misma definición FullHD de doble canal).

## Lo que el firmware NO hace
- **La tensión del panel (12 V) no la elige el firmware**: se elige en la placa (puente/jumper o resistencia junto al
  conector LVDS). Hay que ponerla a mano antes de conectar el panel.
- Los tiempos son los del panel CMO 21,6" de la propia tabla, no los del panel del usuario (modelo desconocido):
  se comprueban contra su hoja de datos.

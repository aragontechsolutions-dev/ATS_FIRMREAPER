# Konka TV4-32K16 — versión "espejo fijo" (TV4-32K16_MIRROR.bin)

Documentación reconstruida el 2026-11-15 a partir del binario (no se recibió su script).

| Dato | Valor |
|---|---|
| Archivo | `TV4-32K16_MIRROR.bin`, SHA-256 `98a919fbef5f848147272fe37a6ccd51bbd0166c0eb22ae8a75125ef211e5fe0` |
| Original | `TV4-32K16.bin`, SHA-256 `32c529658cdefd4beee9fd408a8d52586bbfa7cf4416084a7784f76dcadaad9e` |
| Compresión | Zopfli, 5 iteraciones (se reproduce exactamente) |
| Fuera de la app | Idéntico al original (el logo NO está girado) |
| Estado | Funcionó en el TV (según `README_KONKA_MIRROR_MENU.md`) |

## Cambios en la aplicación (44 bytes)

| Dirección (RAM) | Original | Nuevo |
|---|---|---|
| `0x21DA41` | `bg.sb 0x42b(r1),r0` | `bg.sb 0x42b(r1),r14` (XC_INITDATA.bMirror = 1) |
| `0x2AE569`, `0x2B0E61`, `0x2B1301`, `0x2B13DF`, `0x2B1512` | `bn.lbz r7,0xe(r7)` | `bn.ori r7,r0,0x1` |
| `0x2AE576`, `0x2B0E76`, `0x2B0EE4`, `0x2B132B`, `0x2B13F4`, `0x2B1414` | `bn.lbz r7,0xf(r7)` | `bn.ori r7,r0,0x1` |
| `0x2B0E49`, `0x2B0F40`, `0x2B13C7` | `bn.lwz r7,0xc(r7)` | `bn.movhi r7,0x1` |

Las lecturas `+0x0E`/`+0x0F` son las banderas de espejo H/V del OSD (ver `README_KONKA_MIRROR_MENU.md`, §4);
la versión de menú restaura estas lecturas y fija las banderas desde sus rutinas.

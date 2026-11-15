# Scripts Python de referencia

Scripts originales con los que se hicieron los tres proyectos. Se conservan **sin cambios** como
referencia: el programa en C# debe producir exactamente los mismos archivos (mismas huellas SHA-256).

| Script | Firmware | Guía |
|---|---|---|
| `patch_panel.py` | ATEC 32L18D → panel LP141WX1 | `docs/historial/README_ATEC_LP141WX1.md` |
| `patch_mirror_menu.py` | Konka TV4-32K16 → MIRROR en menú + logo | `docs/historial/README_KONKA_MIRROR_MENU.md` |
| `patch_ir_lg.py` | LG 42LF652VX → mando Soyea v5 | `docs/historial/README_LG_MANDO_SOYEA.md` |
| `build_cave_v2.py` | Ensambla la rutina del LG | necesita `aeonasm.py` y `aeondis.py` (**no recibidos**) |

Notas:
- `patch_panel.py` comprime con zlib -9; los otros con Zopfli (5 iteraciones) si está instalado.
- Sin `zopfli`, los scripts usan zlib -9 y el resultado **no** coincide con las huellas publicadas.

# Plan por etapas

Fecha de inicio: 2026-11-15. Cada etapa se cierra con: código + pruebas automáticas + revisión de
seguridad + documentación, y no modifica lo que ya funciona.

| # | Etapa | Estado |
|---|---|---|
| 1 | **Motor de lectura y verificación** (sin modificar nada) | ✅ Completada |
| 2 | Parches por perfil (LG v5 y Konka) + Zopfli + generar idéntico a los scripts | Pendiente |
| 3 | Logo del Konka (girar / propio / ninguno) y editor de la tabla del mando del LG | Pendiente |
| 4 | Perfil ATEC (panel 1280x800, 6/8 bits) | Pendiente |
| 5 | Manual de usuario, guía para crear perfiles, ejecutable final | Pendiente |

## Etapa 1 — completada

**Hecho**
- Solución .NET 10: `FirmReaper.Core` (motor), `FirmReaper.App` (WPF), `FirmReaper.Cli`, pruebas.
- CRC-32 MSB (poly 0x04C11DB7, init 0) idéntico al de los scripts.
- Descompresor deflate propio que informa del byte exacto donde termina el flujo.
- Lectura validada de cabecera, tabla TEL, MS_IMG_INFO, cola; 11 comprobaciones.
- Catálogo incrustado con 8 imágenes conocidas e identificación (conocido / desconocido válido / inválido).
- Informe en texto y JSON. Ventana WPF con arrastrar y soltar. CLI.
- 52 pruebas (CRC, descompresor, imágenes sintéticas corruptas, catálogo malicioso, 7 firmwares reales).
- Workflow de GitHub Actions que compila, prueba y publica el `.exe` en Windows.

**Comprobado**
- Los 3 binarios recibidos y las 4 salidas regeneradas con los scripts se analizan igual que en Python
  (fin de flash, CRC, tamaño de la app). Las 2 salidas del LG v5 coinciden **exactamente** con las huellas
  del README del proyecto (`17918f8f…`, `3a836aee…`).
- El `.exe` único se publica correctamente para win-x64.

**No comprobado**
- La ventana WPF **no se ha abierto** en Windows (este entorno es Linux: solo se compila). Lo comprobará
  GitHub Actions (compilación) y tú al abrirla.

**Hallazgos nuevos**
- El bloque MS_IMG_INFO (`0x211C7`, tras la marca `BE AD DE`) guarda la **longitud de la app en flash**.
  Los scripts no lo actualizan y los firmwares parcheados funcionaron → el arranque no lo comprueba
  (al menos en LG/Konka/ATEC 8 bits). El programa lo muestra como aviso.
- El 3.er binario (`MST_EXT_SPI_ISP_20230301_153737.BIN`) es el firmware **Soyea** (misma placa que el
  ATEC, contiene `LKK32`), no el del Konka.
- Los 2 bytes de la cola (`F8 71`, `2A 31`, `C8 12`) no son un CRC16 (0x8005/0x1021, init 0/FFFF) del
  comprimido, de la app ni de la zona de la cabecera. Siguen sin explicar.

## Pendiente para la etapa 2

- **Falta el binario original del Konka** (`TV4-32K16.bin`, SHA-256 `32c52965…`) para comprobar que el
  programa genera el mismo archivo que `patch_mirror_menu.py`.
- Zopfli: portar el C del commit `400a507` (el que usa py-zopfli 0.4.3) y comparar byte a byte.
- Formato JSON de perfiles + validación + zonas permitidas + escritura atómica y copia de seguridad.

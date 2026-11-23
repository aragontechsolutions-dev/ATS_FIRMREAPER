# FirmReaper 1.4.0

## Nuevo: placa LM2SA V1.6 (PA20406-2-36ZG), dos firmwares

El programa reconoce y modifica dos firmwares de esta placa:

| Firmware original | SHA-256 | Programa del TV |
|---|---|---|
| `MST_EXT_SPI_ISP_20210531_113958.BIN` | `d0d2b24f4bda26564c62f7c8e1747dbdcddfc500b2ab73b796853be4913cf239` | 16-04-2015 |
| `MST_EXT_SPI_ISP_20210614_165953.BIN` | `aa8b1502e7d88faa0c77f7cef86a5fbf739261846ebd94e02b891e53a978b421` | 22-05-2014 |

Los dos llevan el mismo programa base que el **ATEC 32L18D**, así que tienen sus mismas opciones:

- **Resolución del panel:** 1366x768 (original), 1280x800 (LP141WX1), 1280x720, 1024x768, 800x600.
- **Salida LVDS:** 8 bits (original) o 6 bits + dither.
- **Mando:** ATEC original o **Soyea LKK32CMB**, que también enciende desde reposo (el código del mando cambia en los
  5 sitios, igual que en el ATEC probado).
- **Logo de arranque:** sin cambios, girado 180° o propio (720 × 480).
- **Código de cliente y tabla de botones** editables.
- **Espejo:** no hace falta modificar nada; el menú de fábrica ya tiene **"Mirror Enable"**.

**Sin probar todavía en esta placa.** Con "1280x800 + 8 bits + Mando Soyea" se cambian exactamente los mismos bytes
que en la versión del ATEC que funcionó en el TV (comprobado), pero en otro firmware. Guarda el original y prueba
primero con el programador a mano.

## Sin cambios para los demás TV

Los perfiles del LG, Konka y ATEC generan exactamente los mismos archivos que con la 1.3.0.

## Descargas

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | Línea de comandos (opcional) |
| `MANUAL_USUARIO.pdf` | Manual de usuario (apartado 4.6: placa LM2SA) |
| `FirmReaper-v1.4.0-win-x64.zip` | Todo lo anterior + `SHA256.txt` y `LEEME.txt` |

Windows puede mostrar "Windows protegió su PC" (el programa no está firmado): **Más información → Ejecutar de
todas formas**.

# FirmReaper 1.0.0

Primera versión completa. Programa para Windows 10/11 (64 bits) que analiza volcados SPI de 4 MB de placas de TV
MStar (CPU AEON R2) y genera, verificadas, las versiones modificadas de los firmwares que conoce.
No necesita instalación, internet ni permisos de administrador.

## Descargas

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | Línea de comandos (opcional) |
| `MANUAL_USUARIO.pdf` | Manual de usuario |
| `FirmReaper-v1.0.0-win-x64.zip` | Todo lo anterior + `SHA256.txt` y `LEEME.txt` |

Windows puede mostrar "Windows protegió su PC" (el programa no está firmado): **Más información → Ejecutar de
todas formas**. Comprueba la huella con `certutil -hashfile FirmReaper.exe SHA256` frente a `SHA256.txt`.

## Modificaciones incluidas

| Modelo | Opciones | Resultado idéntico a |
|---|---|---|
| LG 42LF652VX (TP.VST56.PB801) | Mando Soyea LKK32CMB, espejo, tabla del mando editable | `patch_ir_lg.py` v5 |
| Konka TV4-32K16 (TP.VST59S.PB813) | Espejo en el menú de fábrica / fijo; logo girado / propio / sin cambios | `patch_mirror_menu.py` y `TV4-32K16_MIRROR.bin` |
| ATEC 32L18D | Panel LP141WX1 1280x800, 8 bits (funcionó) / 6 bits (no funcionó); mando Soyea (opcional, sin probar) | `patch_panel.py` |

## Seguridad

Antes de escribir nada comprueba: huella del original, bytes originales de cada cambio, que el programa
recomprimido cabe, CRC y estructura del resultado, y que solo cambiaron los bytes previstos. Nunca sobrescribe
archivos y guarda una copia de solo lectura del original.

## Pendiente de confirmar en el TV

- LG v5 (mando Soyea) y encender desde reposo con el mando Soyea (no implementado).
- Konka con el logo sin girar.
- ATEC: que el archivo de 8 bits grabado tenga la huella `ab3d3485…`; la opción de mando Soyea.

Detalle técnico: `docs/ETAPAS.md`. Componentes de terceros: `THIRD_PARTY_NOTICES.md`.

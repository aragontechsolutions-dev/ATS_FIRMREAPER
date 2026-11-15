# FirmReaper 1.1.0

## Novedades

- **Icono propio** para `FirmReaper.exe` y `firmreaper-cli.exe` (antes salía el genérico de Windows).
- **ATEC 32L18D: mando Soyea LKK32CMB (opcional).** El firmware del ATEC es el mismo que el del Soyea (misma
  placa, mismo arranque, misma tabla de botones); solo cambia el código del mando. La nueva opción **Mando**
  ("ATEC original" por defecto / "Soyea") cambia una sola instrucción. Con "Soyea", el mando original del
  ATEC deja de funcionar. **Sin probar en el TV**, incluido el encendido desde reposo.
  Detalle: `docs/historial/README_ATEC_MANDO_SOYEA.md`.

Las opciones por defecto generan los mismos archivos que la 1.0.0.

## Descargas

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | Línea de comandos (opcional) |
| `MANUAL_USUARIO.pdf` | Manual de usuario (actualizado) |
| `FirmReaper-v1.1.0-win-x64.zip` | Todo lo anterior + `SHA256.txt` y `LEEME.txt` |

Windows puede mostrar "Windows protegió su PC" (el programa no está firmado): **Más información → Ejecutar de
todas formas**.

## Pendiente de confirmar en el TV

- LG v5 (mando Soyea) y encender desde reposo con el mando Soyea (no implementado).
- Konka con el logo sin girar.
- ATEC: que el archivo de 8 bits grabado tenga la huella `ab3d3485…`; la opción de mando Soyea.

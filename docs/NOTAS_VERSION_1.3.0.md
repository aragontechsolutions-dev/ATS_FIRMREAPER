# FirmReaper 1.3.0

## ATEC: el mando Soyea ya enciende el TV desde reposo (probado)

Con las versiones 1.1.0 a 1.2.1, el ATEC con "Mando: Soyea" obedecía al mando y apagaba, pero **no se podía volver a
encender** con él. El firmware guarda el código del mando en 5 sitios; faltaba el de la rutina que prepara el
encendido desde reposo. Ahora se cambian todos, igual que en el firmware del Soyea. **Probado en el TV: funciona.**

**Si grabaste un archivo del ATEC con el mando Soyea, genera y graba de nuevo con esta versión.** Con
"1280x800 + 8 bits + Mando Soyea" la huella es `8e797d5dbb8056ec36630b3653eb194a35e08a515e86d404f6a4706ef92aaecd`
(la del archivo probado).

## ATEC: opción "Resolución del panel"

- **1366x768** (original, por defecto), **1280x800** (LP141WX1, probado), **1280x720**, **1024x768** y **800x600**,
  con tiempos estándar (720p, VESA). Las nuevas están **sin probar**: comprueba que tu panel admite esos tiempos.
- La salida LVDS de **8 / 6 bits** es ahora una opción aparte.
- Atención: antes, el perfil del ATEC aplicaba siempre 1280x800; ahora por defecto deja la resolución original. Para
  el LP141WX1 elige "1280x800".

## Descargas

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | Línea de comandos (opcional) |
| `MANUAL_USUARIO.pdf` | Manual de usuario (apartado 4.3: ATEC) |
| `FirmReaper-v1.3.0-win-x64.zip` | Todo lo anterior + `SHA256.txt` y `LEEME.txt` |

Windows puede mostrar "Windows protegió su PC" (el programa no está firmado): **Más información → Ejecutar de
todas formas**.

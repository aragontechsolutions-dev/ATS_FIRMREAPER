# FirmReaper 1.2.0

## Novedades

- **Logo de arranque en el ATEC y en el LG** (nueva opción **Logo de arranque**: sin cambios / girado 180° /
  propio). Hasta ahora solo el Konka lo tenía.
  - ATEC: el logo "atec" (recurso `0x0300`, 2167 × 1228).
  - LG: el logo "LG Life's Good", que está en una zona aparte de la memoria (`0x370000`). El TV lo usa si al
    encender muestra ese logo; si tu TV enciende con una pantalla azul, cambiarlo no tendrá efecto.
- **Logo propio con cualquier imagen** (JPEG, PNG o BMP, de cualquier tamaño) en los tres TV: el programa la
  encaja sin deformarla (con bandas negras si la proporción es otra), la guarda en el mismo formato que el logo
  original y, si no cabe, baja la calidad lo justo. Antes había que preparar un JPEG exacto.
- **Nueva comprobación**: "CRC-16 de los recursos". Cada recurso del firmware termina con un CRC-16; el
  programa lo mantiene correcto cuando cambia el logo del ATEC. En `TV4-32K16_MIRROR_MENU.bin` sale como AVISO
  (lo dejó así el script antiguo) y es normal: ese archivo funciona.
- Girar el logo da exactamente los mismos bytes que Pillow también en 4:2:0 y 4:2:2 (se corrigió la
  decodificación de imágenes con color submuestreado).
- **Mando en los tres TV (LG, Konka y ATEC):**
  - **Código de cliente**: elige a qué mando obedece el TV (2 bytes, p. ej. `37 78`). En el ATEC solo se admiten
    códigos con el segundo byte 7F o menor.
  - **Tabla del mando** (qué hace cada botón) editable también en el Konka y el ATEC, y en los tres se pueden
    **añadir códigos de botones de otro mando**.
  - Línea de comandos: `--cliente "37 78"`; `tabla-mando` funciona con los tres.
  - Si personalizas el mando, el nombre del archivo lo indica (`_cliente-XXXX`, `_tabla-editada`).
- **Espejo:** el ATEC y el LG ya traen el espejo de imagen y menús en su **menú de fábrica** ("Mirror Enable" /
  "MIRROR"); no hace falta modificar el firmware. En el LG, la opción "Espejo" del programa solo cambia el valor
  inicial y ahora lo dice (el menú de fábrica manda).

Las opciones por defecto generan los mismos archivos que la 1.1.0 (incluidas todas las versiones probadas).

## Descargas

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | Línea de comandos (opcional) |
| `MANUAL_USUARIO.pdf` | Manual de usuario (actualizado: apartado 4.5, logo) |
| `FirmReaper-v1.2.0-win-x64.zip` | Todo lo anterior + `SHA256.txt` y `LEEME.txt` |

Windows puede mostrar "Windows protegió su PC" (el programa no está firmado): **Más información → Ejecutar de
todas formas**.

## Pendiente de confirmar en el TV

- Logos girados o propios del ATEC y del LG.
- Códigos de cliente elegidos y tablas del mando editadas (los tres TV).
- LG v5 (mando Soyea) y encender desde reposo con el mando Soyea (no implementado).
- Konka con el logo sin girar.
- ATEC: que el archivo de 8 bits grabado tenga la huella `ab3d3485…`; la opción de mando Soyea.

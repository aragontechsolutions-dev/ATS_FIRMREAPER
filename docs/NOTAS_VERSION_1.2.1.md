# FirmReaper 1.2.1

## Corrección: ATEC + mando Soyea no encendía desde reposo

Con la 1.1.0 y la 1.2.0, el ATEC con "Mando: Soyea" (o con un código de cliente elegido) obedecía al mando recién
grabado, pero **después de apagarlo no se podía volver a encender con ese mando**.

- **Causa:** el firmware del ATEC guarda el código del mando en dos sitios: el que usa con el TV encendido (que sí se
  cambiaba) y una copia para el receptor de infrarrojos en reposo, que decide qué mando enciende el TV (que no se
  cambiaba).
- **Corrección:** ahora se cambian los dos, igual que están en el firmware del Soyea. Respecto al archivo anterior
  solo cambian 2 bytes del programa.
- El programa rechaza ahora cualquier perfil que cambie el código del mando solo en parte.
- **LG y Konka no tenían este problema.**

**Si grabaste un archivo del ATEC con el mando Soyea, genera y graba de nuevo con esta versión.** Con las opciones
"8 bits + Mando Soyea" la huella del archivo nuevo es `0b1bd2019841865cc875d8472eb9affb515d971dcbdc0759f35e2314915d0532`.

Pendiente de probar en el TV: encender desde reposo con el mando Soyea.

## Descargas

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | Línea de comandos (opcional) |
| `MANUAL_USUARIO.pdf` | Manual de usuario |
| `FirmReaper-v1.2.1-win-x64.zip` | Todo lo anterior + `SHA256.txt` y `LEEME.txt` |

Windows puede mostrar "Windows protegió su PC" (el programa no está firmado): **Más información → Ejecutar de
todas formas**.

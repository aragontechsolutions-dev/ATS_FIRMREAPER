# FirmReaper 1.1 — Manual de usuario

Programa para Windows que **analiza** volcados de firmware de placas de TV MStar (memoria SPI de 4 MB) y
**genera versiones modificadas** de los firmwares que conoce, verificando todo antes de escribir.

> **Aviso.** Grabar un firmware puede dejar el TV sin arrancar. Guarda siempre el original (el programa hace
> una copia, pero conviene tener otra) y ten a mano el programador SPI para volver atrás. El inversor CCFL
> trabaja con alta tensión (~675 V). Úsalo solo en equipos propios o con permiso de su dueño.

---

## 1. Qué hace y qué no

**Sí hace:**

- Abre un volcado `.BIN` de 4 MB y comprueba que su estructura es correcta (cabecera, CRC, programa
  comprimido, tabla de recursos…).
- Lo **identifica** por su huella SHA-256: original conocido, versión ya generada (y si funcionó en el TV),
  firmware de referencia o **no reconocido**.
- Para los **originales conocidos**, genera la versión modificada que elijas:

| Modelo | Placa | Modificaciones |
|---|---|---|
| LG 42LF652VX | TP.VST56.PB801 | Mando Soyea LKK32CMB (sí/no), espejo de imagen (original/desactivado), tabla del mando editable |
| Konka TV4-32K16 | TP.VST59S.PB813 | Espejo (opción MIRROR en el menú de fábrica / fijo), logo de arranque (girado 180° / propio / sin cambios) |
| ATEC 32L18D | BD_MST061B_10AZU_14014 | Panel LG.Philips LP141WX1 1280x800 (salida LVDS de 8 o 6 bits); mando Soyea LKK32CMB (opcional) |

**No hace:**

- **No modifica firmwares desconocidos.** Cada modificación depende de direcciones encontradas analizando ese
  binario concreto. Ante otro archivo dirá "NO RECONOCIDO" y no ofrecerá cambios.
- No lee ni graba la memoria de la placa: para eso usas tu programador (CH341A, RT809H…).
- No necesita internet ni permisos de administrador.

---

## 2. Requisitos e instalación

- Windows 10 u 11 de 64 bits. No hay que instalar nada (el programa lleva .NET dentro).
- Unos 300 MB libres (el `.exe` ocupa ~135 MB y la primera vez prepara archivos internos).

**Archivos que se entregan:**

| Archivo | Para qué |
|---|---|
| `FirmReaper.exe` | El programa con ventana (doble clic) |
| `firmreaper-cli.exe` | La versión de línea de comandos (opcional, para cmd/PowerShell) |
| `MANUAL_USUARIO.pdf` | Este manual |
| `SHA256.txt` | Huellas de los `.exe`, para comprobar que la descarga está bien |
| `LEEME.txt` | Qué es cada archivo |

**Primer arranque:**

1. Copia `FirmReaper.exe` a una carpeta tuya (por ejemplo `C:\FirmReaper`).
2. Doble clic. La primera vez tarda unos segundos más.
3. Si Windows muestra **"Windows protegió su PC"** (SmartScreen): pulsa **Más información → Ejecutar de todas
   formas**. Aparece porque el programa no está firmado digitalmente; no es un virus.

Para comprobar la descarga (opcional), en una ventana de comandos:

```
certutil -hashfile FirmReaper.exe SHA256
```

El resultado debe coincidir con el de `SHA256.txt`.

---

## 3. La ventana principal

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [Abrir .BIN…] [Guardar informe…] [Copiar informe]                            │
├──────────────────────────────────────────────────────────────────────────────┤
│ Identificación (verde / amarillo / rojo)                                     │
│ Ruta · SHA-256 · Placa · SBOOT                                               │
├──────────────────────────────────────────────────────────────────────────────┤
│ Comprobaciones │ Generar │ Cambios (hex) │ Recursos (TEL) │ Informe completo │
└──────────────────────────────────────────────────────────────────────────────┘
```

Para abrir un volcado: botón **Abrir .BIN…** o **arrastra** el archivo a la ventana.

### 3.1 La franja de identificación

| Color | Significado |
|---|---|
| **Verde** | Archivo conocido y válido: original, versión nuestra o referencia |
| **Amarillo** | **No reconocido** pero con estructura válida: modificado por otra persona o firmware no analizado. No se puede modificar |
| **Rojo** | Archivo dañado, mal leído o que no es de este tipo |

Mensajes posibles:

- **"ORIGINAL reconocido: …"** → se puede modificar (pestaña Generar).
- **"VERSIÓN NUESTRA: … (probado en el TV: funcionó / NO funcionó / pendiente de probar)"** → es un archivo
  ya generado. Sirve para comprobar que lo que vas a grabar es exactamente esa versión.
- **"REFERENCIA: …"** → firmware usado solo como fuente de información (Soyea).
- **"NO RECONOCIDO…"** → ver colores amarillo/rojo.

### 3.2 Pestaña "Comprobaciones"

Lista de 11 comprobaciones del archivo. Cada fila sale como OK, AVISO (amarillo) o ERROR (rojo):

| Comprobación | Qué mira |
|---|---|
| Tamaño de 4 MB | Que el volcado mida exactamente 4 194 304 bytes |
| Inicio de la app / fin en RAM | Que la cabecera del programa (en 0x20000) sea coherente |
| **CRC-32 de la aplicación** | Que el programa no esté dañado. Si falla, **el TV no arrancaría** con ese archivo |
| Tabla de recursos TEL | La lista de logos, fuentes, etc. |
| La app no pisa los recursos | Que el programa comprimido termine antes del primer recurso |
| Descompresión del programa | Que el programa comprimido se pueda descomprimir entero |
| Tamaño en la cola / fin en flash | Coherencia entre el programa y la cabecera |
| Bloque MS_IMG_INFO | Un dato informativo. En firmwares ya modificados sale como **AVISO**: es normal |
| Espacio libre tras la app | Los bytes libres entre el programa y los recursos |

**Consejo:** si lees la placa varias veces y una lectura da ERROR de CRC, vuelve a leerla: suele ser una
mala conexión de la pinza.

### 3.3 Pestañas "Recursos (TEL)" e "Informe completo"

- **Recursos (TEL):** los recursos del firmware (el logo de arranque aparece como *JPEG*).
- **Informe completo:** todo el análisis en texto. **Guardar informe…** lo guarda en `.txt` o `.json`.
  **Copiar informe** lo copia al portapapeles.

---

## 4. Generar un firmware modificado

1. Abre el **original** (franja verde, "ORIGINAL reconocido").
2. Ve a la pestaña **Generar**.
3. Elige las opciones (ver apartados 4.1 a 4.3).
4. Pulsa **Generar…** y elige la **carpeta** donde guardar el resultado.
5. Espera (de 1 a 15 segundos según el modelo). El registro muestra cada paso. **Cancelar** detiene el
   proceso sin guardar nada.
6. Al terminar aparece "Firmware generado y verificado" con su ruta y su huella SHA-256.

**Antes de guardar, el programa comprueba todo esto. Si algo falla, no guarda nada:**

- que el archivo es exactamente el original del perfil (huella SHA-256);
- que los bytes que se van a cambiar son los esperados;
- que el programa recomprimido **cabe** en su espacio;
- que el archivo final es válido (CRC, cabecera, descompresión);
- que **solo cambiaron los bytes previstos**, y que todo lo demás (arranque, recursos, ajustes) es idéntico.

Si eliges exactamente las opciones de una versión ya probada, el registro dice
**"Idéntico a la versión ya generada y documentada (…)"**: el archivo es byte a byte el mismo que ya funcionó.

### 4.1 LG 42LF652VX

| Opción | Valores |
|---|---|
| Mando Soyea LKK32CMB | **Sí (v5)** / No (mando LG original) |
| Espejo de imagen | **Como el original (activado)** / Desactivado |
| Tabla del mando | Solo con "Mando Soyea: Sí". **Editar…** abre el editor (4.4) |

- Con "Sí" + "Como el original" sale `LG_42LF652VX_MANDO_SOYEA_v5.BIN`.
- Con "Sí" + "Desactivado" sale `LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN`.
- **Pendiente de probar en el TV:** encender desde reposo con el mando Soyea (el modo de reposo no se modificó).

### 4.2 Konka TV4-32K16

| Opción | Valores |
|---|---|
| Modo de espejo | **Opción MIRROR en el menú de fábrica** / Espejo fijo (siempre activado) |
| Logo de arranque | **Girado 180°** / Sin cambios / Propio |

- Por defecto (menú + girado) sale `TV4-32K16_MIRROR_MENU.bin`, **la versión probada en el TV**.
- "Espejo fijo" + "Sin cambios" da `TV4-32K16_MIRROR.bin` (la primera versión probada).
- Con "MIRROR en el menú": en el menú de fábrica (Menu + 8896), donde estaba **"Logo"** ahora pone **"MIRROR"**.
  ON = imagen y menús espejados; OFF = normal. **El cambio se aplica al apagar y volver a encender.**
- El logo de arranque se dibuja **antes** de que actúe el espejo; por eso se gira 180°. Con MIRROR en OFF el
  logo girado saldría al revés.

**Logo propio:** al pulsar Generar… el programa pide el archivo. Debe ser:

- JPEG **baseline** (no "progresivo"), en color;
- de **exactamente 1366 × 768** píxeles;
- de **como máximo 131 381 bytes**;
- sin datos añadidos después del final del JPEG.

Si no cumple, el programa dice por qué (por ejemplo *"Logo: debe medir 1366x768 (mide 800x600)"*).
Truco: en cualquier editor de imágenes, "Guardar como JPEG" con calidad 80–90 % y **sin** "progresivo".

### 4.3 ATEC 32L18D

| Opción | Valores |
|---|---|
| Salida LVDS | **8 bits (probado: FUNCIONÓ)** / 6 bits + dither (probado: NO funcionó) |
| Mando | **ATEC original** / Soyea LKK32CMB (sin probar en el TV) |

- Cambia la entrada del panel a 1280x800 (LP141WX1). La salida de 8 bits necesita **mapeo JEIDA**; el par 3
  de datos queda sin conectar.
- Tras grabarlo: menú de fábrica → *Restore/Reset factory*, por si la base de datos guarda valores del
  panel anterior.
- **Mando Soyea:** el firmware del ATEC es el mismo que el del Soyea (misma placa) y su tabla de botones es
  idéntica; solo cambia el código del mando. Con esta opción el TV obedece al mando Soyea **y deja de obedecer
  al mando original del ATEC**. Encender desde reposo con el mando Soyea no está comprobado.

### 4.4 Editor de la tabla del mando (LG)

Pestaña Generar → *Tabla del mando* → **Editar…**

- Una fila por cada botón del mando Soyea (48). En la columna **Función en el TV** eliges qué hace.
  *(sin función)* = el botón no hace nada.
- Máximo **44** botones con función (el contador abajo se pone en rojo si te pasas).
- Los botones con función **VOLUME_±** o **CHANNEL_±** hacen además de flechas dentro de los menús y del USB.
- **Restaurar la del perfil** vuelve a la tabla v5. **Importar… / Exportar…** guardan la tabla en `.json`.
- Algunas funciones salen como *"función LG 0xNN (sin identificar)"*: existen en el TV pero no se sabe qué
  hacen. Úsalas solo para probar.
- **Una tabla editada no está probada en el TV.** El informe lo indica ("EDITADA") y ya no la compara con la v5.

---

## 5. Archivos que se guardan

En la carpeta que elijas:

| Archivo | Contenido |
|---|---|
| `NOMBRE.BIN` | El firmware generado. Si ya existe, se llama `NOMBRE_2.BIN`, `_3`… **Nunca se sobrescribe nada** |
| `NOMBRE.BIN.informe.txt` | Informe legible: opciones, huellas, cabecera antes/después, cada cambio, comprobaciones y procedimiento de grabación |
| `NOMBRE.BIN.informe.json` | Los mismos datos para otros programas |
| `respaldo_originales\…_<huella>.BIN` | **Copia de solo lectura del original** (solo la primera vez) |

El archivo original que abriste **no se modifica nunca**.

### Pestaña "Cambios (hex)"

Después de generar, muestra cada cambio: dirección, bytes originales y nuevos en hexadecimal. Los bytes
que cambian aparecen **en rojo**.

---

## 6. Grabar el firmware en la placa

1. **Lee la memoria 3 veces** con el programador y comprueba que las 3 lecturas son idénticas (o ábrelas en
   FirmReaper: deben dar la misma huella). Guarda el original en 2 sitios.
2. Abre la lectura en FirmReaper: debe salir **"ORIGINAL reconocido"** (verde).
3. Genera la versión que quieras (apartado 4).
4. Comprueba la huella del archivo a grabar: ábrelo en FirmReaper → debe salir "VERSIÓN NUESTRA" o, si es una
   combinación nueva, la huella debe coincidir con la del informe.
5. En el programador: **BORRAR → ESCRIBIR → VERIFICAR**.
   - **CH341A negro:** algunas unidades sacan 5 V en las líneas de datos. Comprueba que tu unidad trabaja a
     3,3 V (o que tiene la modificación) antes de conectarla a la memoria.
6. Primer arranque: LED de reposo, respuesta al mando, imagen y menús.
7. **Si no arranca:** vuelve a grabar el original (está en `respaldo_originales`).

**Seguridad eléctrica:**

- **El inversor CCFL** trabaja a unos 675 V: aíslalo y no lo toques con el equipo encendido.
- **Panel nuevo (ATEC):** el VCC del LP141WX1 admite como máximo 4,0 V. Mide 3,3 V en el conector antes de
  conectarlo.

---

## 7. Si algo falla

### Mensajes al generar

| Mensaje | Qué significa / qué hacer |
|---|---|
| *La huella del archivo (…) no es la del original del perfil (…)* | No es el original esperado: otra lectura, otro firmware o ya modificado. Vuelve a leer la placa |
| *Parche 0x…: el archivo YA tiene este cambio* | Ese archivo ya está modificado. Usa el original |
| *Parche 0x…: los bytes originales no coinciden* | El archivo no es el esperado en ese punto. No se toca nada |
| *Con estas opciones no hay nada que cambiar…* | Las opciones elegidas dejan el firmware igual que el original |
| *La app recomprimida no cabe…* | El programa modificado no entra en su hueco. No debería pasar con los perfiles incluidos |
| *Falta el archivo JPEG del logo propio* | Elegiste "Propio" pero no indicaste el archivo |
| *Logo: …* | El JPEG propio no cumple los requisitos (4.2) |
| *Tabla del mando no válida: …* | Hay botones repetidos, funciones no permitidas o más de 44 teclas |
| *El archivo original cambió en disco después de analizarlo…* | Se modificó el `.BIN` mientras usabas el programa. Ábrelo de nuevo |
| *Ya existe una copia de seguridad con ese nombre y distinto contenido* | En `respaldo_originales` hay un archivo con el mismo nombre pero diferente. Revísalo |

### El programa se cierra o muestra "error inesperado"

El detalle queda en `%LOCALAPPDATA%\FirmReaper\errores.log` (pega esa ruta en el Explorador). Envía ese
archivo para revisarlo.

### Solo aparece una ventana negra que se cierra

Estás abriendo `firmreaper-cli.exe` (la línea de comandos). El programa con ventana es **`FirmReaper.exe`**.

---

## 8. Línea de comandos (opcional)

Desde cmd o PowerShell, en la carpeta de `firmreaper-cli.exe`:

| Comando | Para qué |
|---|---|
| `firmreaper-cli info ARCHIVO.BIN` | Análisis completo (añade `--json` para JSON) |
| `firmreaper-cli verificar A.BIN B.BIN …` | Una línea por archivo (útil para comparar lecturas) |
| `firmreaper-cli catalogo` | Versiones conocidas |
| `firmreaper-cli perfiles` | Modificaciones disponibles y sus opciones |
| `firmreaper-cli generar ORIGINAL.BIN --perfil ID [--opcion clave=valor …] [--logo LOGO.jpg] [--tabla-mando TABLA.json] --salida CARPETA` | Generar |
| `firmreaper-cli tabla-mando --perfil lg-42lf652vx-soyea --salida tabla.json` | Exportar la tabla del mando por defecto |

Ejemplos:

```
firmreaper-cli generar MST_EXT_SPI_ISP_20220511_165639.BIN --perfil lg-42lf652vx-soyea --opcion espejo=off --salida C:\salida
firmreaper-cli generar TV4-32K16.bin --perfil konka-tv4-32k16-mirror --opcion logo=propio --logo milogo.jpg --salida C:\salida
firmreaper-cli generar Atec_32_KPC_Salva.BIN --perfil atec-32l18d-lp141wx1 --opcion bits=8 --salida C:\salida
```

Códigos de salida: `0` correcto · `1` no válido / no reconocido / error · `2` uso incorrecto.

---

## 9. Añadir otros modelos (perfiles)

Las modificaciones se describen en **perfiles** (archivos `.json`). Puedes añadir uno sin recompilar: crea la
carpeta `perfiles` junto a `FirmReaper.exe` y copia ahí el `.json`. El programa lo revisa al arrancar y
**rechaza** los perfiles dañados o que intenten escribir fuera de sus zonas permitidas.

Crear un perfil requiere analizar el firmware (direcciones, bytes originales). Está explicado en la
**guía técnica** `docs/PERFILES.md`.

---

## 10. Versiones conocidas

| Archivo | SHA-256 (inicio) | Estado |
|---|---|---|
| LG original `MST_EXT_SPI_ISP_20220511_165639.BIN` | `ef5c8055…` | Original |
| `LG_42LF652VX_MANDO_SOYEA_v5.BIN` | `17918f8f…` | Pendiente de probar |
| `LG_42LF652VX_MANDO_SOYEA_v5_ESPEJO_OFF.BIN` | `3a836aee…` | Pendiente de probar |
| Konka original `TV4-32K16.bin` | `32c52965…` | Original |
| `TV4-32K16_MIRROR_MENU.bin` (menú + logo girado) | `81bd3c8b…` | Funcionó |
| `TV4-32K16_MIRROR.bin` (espejo fijo) | `98a919fb…` | Funcionó |
| `TV4-32K16_MIRROR_MENU_SIN_LOGO.bin` | `3bffdfd0…` | Pendiente de probar |
| ATEC original `Atec_32_KPC_Salva.BIN` | `19491133…` | Original |
| `Atec32L18D_LP141WX1_8bit.BIN` | `ab3d3485…` | Funcionó (huella del archivo grabado sin confirmar) |
| `Atec32L18D_LP141WX1_6bit.BIN` | `7bd55c39…` | NO funcionó |
| Soyea `MST_EXT_SPI_ISP_20230301_153737.BIN` | `fc9fed8f…` | Referencia (no se modifica) |

Las huellas completas aparecen en el programa (franja de identificación) y en `firmreaper-cli catalogo`.

---

## 11. Preguntas frecuentes

**¿Puedo usarlo con otro TV MStar?**
Solo si existe un perfil para su firmware exacto. Con otros archivos, el programa solo analiza.

**¿Por qué el archivo generado es idéntico al que ya tenía?**
Porque el programa reproduce exactamente los scripts con los que se hicieron y probaron esas versiones. Así
sabes que grabas lo mismo que ya funcionó.

**¿Necesita internet?**
No. Todo va dentro del `.exe`.

**¿Dónde están mis ajustes?**
El programa no guarda ajustes. Solo crea `%LOCALAPPDATA%\FirmReaper\errores.log` si hay un error.

---

*FirmReaper 1.1.0 — ATS. Componentes de terceros: ver `THIRD_PARTY_NOTICES.md` (Zopfli, zlib, LibJpeg.NET,
tablas AEON).*

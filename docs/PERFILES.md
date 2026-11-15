# Guía: cómo crear un perfil

Un **perfil** describe las modificaciones de UN firmware concreto (identificado por su SHA-256).
El programa no "adivina" nada: cada parche depende de direcciones encontradas analizando ese binario.

## Dónde van

| Tipo | Ubicación | Notas |
|---|---|---|
| Integrado | `src/FirmReaper.Core/Profiles/Builtin/*.json` | Va dentro del programa (hay que recompilar). |
| Externo | carpeta `perfiles\` junto a `FirmReaper.exe` | Se carga al iniciar, **sin recompilar**. No puede usar el `id` de uno integrado. |

Los integrados de los scripts se generan con `python3 tools/perfiles/generar_perfiles.py` (los bytes se
leen de los scripts de referencia; no se copian a mano).

## Formato (versión 1)

```json
{
  "formato": 1,
  "id": "lg-42lf652vx-soyea",
  "version": 5,
  "nombre": "LG 42LF652VX (TP.VST56.PB801) — mando Soyea LKK32CMB",
  "descripcion": "Qué hace el perfil.",
  "estado": "verificado",
  "original": { "sha256": "ef5c8055…" },
  "app": { "baseRam": "0x243800" },
  "compresion": { "metodo": "zopfli", "iteraciones": 5 },
  "zonasPermitidas": [
    { "desde": "0x25E644", "hasta": "0x25E648", "motivo": "instrucción que fija bMirror" }
  ],
  "parchesFijos": [],
  "opciones": [
    { "id": "espejo", "etiqueta": "Espejo de imagen", "tipo": "lista", "porDefecto": "original",
      "valores": [
        { "id": "original", "etiqueta": "Como el original", "parches": [] },
        { "id": "off", "etiqueta": "Desactivado", "parches": [
          { "direccion": "0x25E644", "original": "f8a10163", "nuevo": "f8010163",
            "descripcion": "bMirror = 0" } ] }
      ] }
  ],
  "salidasConocidas": [
    { "opciones": { "espejo": "off" }, "sha256": "…", "archivo": "NOMBRE.BIN" }
  ]
}
```

| Campo | Significado |
|---|---|
| `formato` | Siempre `1`. |
| `id` | Minúsculas, dígitos y guiones (máx. 64). Único. |
| `estado` | `verificado` (comprobado con el binario real) o `sin-verificar` (se avisa en pantalla y en el informe). |
| `original.sha256` | Huella del volcado ORIGINAL al que se aplica. Con otro archivo, el programa se niega. |
| `app.baseRam` | Dirección de RAM del primer byte de la app descomprimida = destino RAM (cabecera +0x04) + `0x3800`. Se comprueba contra el firmware. |
| `compresion` | `{"metodo":"zopfli","iteraciones":5}` (LG, Konka) o `{"metodo":"zlib","nivel":9}` (ATEC; idéntico a zlib 1.3). Debe ser el mismo compresor que usó el script para que el resultado coincida con la versión probada. |
| `zonasPermitidas` | Rangos `[desde, hasta)` en RAM donde se permite escribir, con su `motivo`. Máx. 64 KB cada una, sin solaparse, dentro de la app. |
| `parchesFijos` | Se aplican siempre. |
| `opciones` | Tipo `lista`: el usuario elige un valor; cada valor trae sus parches (puede no traer ninguno). |
| parche | `direccion` (RAM, `0x…`), `original` y `nuevo` (hex en minúsculas, misma longitud, máx. 4096 bytes, distintos), `descripcion`. |
| `salidasConocidas` | Huella del resultado para una combinación de opciones (todas las opciones definidas). Si coincide, se informa "idéntico a la versión documentada". |
| `logo` (opcional) | Dónde está el logo de arranque y cómo es el original. **Uno y solo uno** de: `idTel` (recurso de la tabla TEL, `0x…`; con `crc16: true` si el recurso termina en CRC-16 que hay que mantener) o `zonaFlash` (`desde`, `hasta` = dirección de **flash**, no de RAM; `relleno`, normalmente `0xFF`). Además `longitudJpeg` (bytes del JPEG original hasta FFD9 incluido), `ancho`, `alto`. |
| valor `.logo` (opcional) | `"girar"` (180°, re-codificado en el formato del original, idéntico a Pillow) o `"propio"` (imagen del usuario: JPEG, PNG o BMP). Sin el campo, el logo no cambia. Solo UNA opción puede actuar sobre el logo. |

| `tablaMando` (opcional) | Tabla botón → función editable: `direccion`, `entradas`, `prefijoCodigo`, `opcion`/`valor` (el valor de opción que la aplica), `funciones` (`indice` + `nombre`, las únicas permitidas) y `botones` (`codigo` + `nombre`). |

### La tabla del mando

- Debe coincidir EXACTAMENTE (dirección y longitud) con un parche del valor `opcion`=`valor`. El editor solo
  cambia los bytes "nuevos" de ese parche: la zona permitida y los bytes originales se siguen comprobando.
- Formato en el firmware (LG TP.V56): `entradas` × 8 bytes: u32 LE `prefijoCodigo | código`, u8 índice, 3 bytes a 0;
  el resto, terminador `FFFF0000 FF000000`. El firmware usa la PRIMERA coincidencia: no se admiten botones repetidos.
- Una entrada queda siempre para el terminador (LG: máximo 44 teclas).
- La tabla por defecto es la del parche (la v5 probada). Con una tabla editada el resultado ya no es la versión
  documentada (el informe lo indica y lista la tabla).
- Archivo de tabla (importar/exportar, `firmreaper-cli tabla-mando` / `--tabla-mando`):
  `{"formato":1,"perfil":"<id>","asignaciones":[{"codigo":"0x45","indice":"0x26","boton":"SLEEP","funcion":"SLEEP"}]}`
  (`boton` y `funcion` son informativos).

### El logo

**Antes de tocar nada** el programa comprueba en el original: que en el hueco hay un JPEG de exactamente
`longitudJpeg` bytes y `ancho`×`alto`; que el resto del hueco tiene el relleno esperado; y, con `crc16`, que el
CRC-16 original es correcto. Si algo no cuadra, no genera nada (así un perfil mal escrito no puede pisar datos).

Tres formas de hueco:

| Tipo | Hueco donde va el JPEG nuevo | Relleno | Ejemplo |
|---|---|---|---|
| `idTel` sin `crc16` | `longitudJpeg` bytes al inicio del recurso; lo que sigue en el recurso se conserva | `00` | Konka: `{"idTel":"0x065E","longitudJpeg":131381,"ancho":1366,"alto":768}` (así lo hacía el script; las versiones probadas salen idénticas) |
| `idTel` con `crc16: true` | todo el recurso menos sus 2 últimos bytes; después se recalcula el CRC-16 | `00` | ATEC: `{"idTel":"0x0300","crc16":true,"longitudJpeg":49128,"ancho":2167,"alto":1228}` |
| `zonaFlash` | `[desde, hasta)`; debe estar después de la app, de todos los recursos y del fin del área TEL (campo `0x22088`) | `relleno` | LG: `{"zonaFlash":{"desde":"0x370000","hasta":"0x3B0000","relleno":"0xFF"},"longitudJpeg":27753,"ancho":1376,"alto":768}` |

**Formato del logo nuevo.** Siempre el del original: mismas dimensiones, mismo submuestreo (4:4:4, 4:2:2 o 4:2:0),
mismas tablas de cuantización y mismo intervalo de reinicio. Así el TV recibe un JPEG como el que ya sabe mostrar.

- **Girar:** se decodifica el original, se gira y se vuelve a codificar con sus tablas. Para el Konka, ATEC y LG el
  resultado es byte a byte el de Pillow 12.3.0 (`im.convert("RGB").rotate(180).save(..., qtables=im.quantization,
  subsampling=…, restart_marker_blocks=…)`).
- **Propio:** la imagen del usuario (JPEG, PNG o BMP, cualquier tamaño):
  - si ya es un JPEG baseline con el formato exacto (dimensiones y submuestreo) y cabe, se escribe **tal cual**;
  - si no, se encaja en `ancho`×`alto` **sin deformarla** (centrada, con bandas negras si la proporción es otra) y se
    codifica en el formato del original.
- **Si no cabe**, se baja la calidad por pasos: tablas del original ×1,25 … ×10 y luego tablas JPEG estándar a
  calidad 50, 25, 10, 5 y 1. El informe dice qué paso se usó. Si ni así cabe, se rechaza.
- Antes de escribir se valida el JPEG final (baseline, en color, dimensiones y submuestreo del original, nada tras el
  FFD9, cabe y se decodifica entero) y, después de escribir, que fuera del hueco (y del CRC) el archivo no cambió.

## Reglas que comprueba el programa al cargar (si falla una, el perfil se rechaza entero)

1. JSON estricto: campos desconocidos, duplicados, comentarios o comas finales → error.
2. Cada parche **dentro** de una zona permitida (entero, sin salirse por el final).
3. Ningún parche se solapa con otro que pueda aplicarse a la vez (fijos y los de opciones distintas).
4. Zonas dentro de la app, sin solaparse, de tamaño limitado y con motivo.
5. Huellas, direcciones y hexadecimal bien formados; valor por defecto existente; ≥ 2 valores por opción.

## Reglas al generar (si falla una, no se escribe nada)

1. Contenedor válido y huella = `original.sha256`; base de la app coincide.
2. Los bytes `original` de cada parche están en el firmware (si ya están los `nuevo`, avisa "ya tiene este cambio").
3. Recompresión reversible y que **cabe** antes del primer recurso.
4. Nueva cabecera y CRC; la salida se vuelve a analizar entera.
5. Solo cambiaron los bytes de los parches; fuera de la app el archivo es idéntico al original.

## Cómo obtener las direcciones

Con los scripts de análisis (Python) y el desensamblador de `tools/aeon` (`aeondis.py`). Ver las guías de
`docs/historial/` como ejemplo de cómo se documentó cada cambio.

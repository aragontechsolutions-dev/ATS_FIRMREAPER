# Konka "TV4-32K16" — MIRROR en el menú de fábrica + logo corregido

Guía técnica. Fecha: 2026-11-14. Sustituye a la versión "espejo fijo" (`TV4-32K16_MIRROR.bin`), que funcionó en el TV.

## 1. Resultado

| Archivo | Descripción |
|---|---|
| `TV4-32K16_MIRROR_MENU.bin` | Firmware para grabar. SHA-256 `81bd3c8b…160b93570` |
| `patch_mirror_menu.py` | Lo genera desde el original y se verifica solo (`pip install zopfli pillow`) |

**Uso en el TV:**
- Menú de fábrica (Menu + 8896, como antes), en la página donde estaba **"Logo"**: ahora pone **"MIRROR"** (OFF/ON).
- **ON** (valor de fábrica): imagen y menús espejados, como el firmware probado.
- **OFF**: imagen normal, para un panel montado al derecho.
- **El cambio se aplica al apagar y volver a encender** (el valor se guarda en la base de datos).

## 2. Por qué se reutiliza "Logo"

- La variable de "Logo" (`stGenSetting + 0x50F` = `0x3EA57F`) solo la usaban el menú (mostrar y cambiar) y la rutina de valores por defecto.
- **Ningún otro código la leía**: ni la aplicación ni el arranque (el SBOOT solo contiene el descompresor). Era una opción muerta.
- Se guarda en la base de datos (bloque `stGenSetting` de 0x730 bytes, id 0x132). Su valor por defecto es 1, así que MIRROR queda en ON tras un "Restore Factory Default".
- La etiqueta "Logo" (5 bytes) no tiene sitio para "MIRROR". El puntero de la tabla del menú (`0x32A0B4`) apunta ahora a un texto nuevo.

## 3. Por qué hacen falta dos ganchos (orden de arranque)

```
main → 0x26B8C6 → 0x25E814 (arranque)
   0x25E908  0x22923F  inicia sistema:
                 0x229273  XC init  (bMirror lo fija el parche 0x21DA41 = 1)
                 0x2292F9  GOP init → 0x2B29A7 crea contexto GOP   ← GANCHO 2
   0x25E961  0x264956  carga stGenSetting desde la BD
   0x25E965  0x25DA52  valida stGenSetting                          ← GANCHO 1
   …          interfaz, fuente de vídeo, ventanas
```

- La base de datos se carga **después** de iniciar XC y GOP, así que no se puede leer MIRROR en ese momento.
- La solución es arrancar siempre en el estado probado (espejo activado) y, en cuanto se carga la base de datos, **desactivarlo si MIRROR está en OFF**.
- Entre el inicio de XC/GOP y el gancho 1 no se configura ninguna ventana de vídeo ni de interfaz. Se comprobó con el grafo de llamadas; las únicas rutas que aparecían eran de error o reinicio.

## 4. Estado que controla el espejo (localizado en el binario)

| Dato | Dirección | Uso |
|---|---|---|
| Copia global `XC_INITDATA` | `0x3F7780` (bMirror = `0x3F779B`) | la leen en tiempo de ejecución las funciones de ventana (`0x2F49C4`, `0x2F4C94`, `0x2F4E9E`, `0x305FAE`…) |
| Modo espejo del XC | `*(*(0x3D84B4)+0xC)+0xB8` | MApi_XC_Init escribe 3 (H+V) si bMirror = 1 (`0x304C26`) |
| Banderas del OSD | `*(*(0x3D5AA8))` +0x0E (H) / +0x0F (V) | 13 lecturas en la librería GOP; ninguna escritura en el firmware |
| MIRROR (antes Logo) | `0x3EA57F` | menú `0x27C024/0x27C032`, guardado `0x264ABD` |

## 5. Cambios en la aplicación (137 bytes)

| Dirección | Cambio |
|---|---|
| `0x21DA41` | `sb 0x42b(r1),r0` → `sb 0x42b(r1),r14`: bMirror = 1 al iniciar XC |
| `0x25E965` | `jal 0x25DA52` → `jal 0x33B214` (rutina 1) |
| `0x2B29A7` | `jal 0x2AE8CB` → `jal 0x33B25C` (rutina 2) |
| `0x32A0B4` | puntero de etiqueta `0x337116` ("Logo") → `0x33B20C` ("MIRROR") |
| `0x33B20C`–`0x33B288` | "MIRROR" + rutinas, dentro de textos de depuración JPEG sin uso (`0x33AB66`–`0x33B9A1`: ninguna referencia desde código ni datos) |

**Rutina 1** (después de cargar la BD):

```
movhi r7,0x3f ; lbz r7,-0x5a81(r7)       ; MIRROR
bnei  r7,0,FIN                           ; ON: nada que hacer
movhi r6,0x3f ; sb 0x779b(r6),r0         ; bMirror (copia XC) = 0
movhi r6,0x3e ; lwz r6,-0x7b4c(r6) ; beqi r6,0,GOP
lwz r6,0xc(r6) ; beqi r6,0,GOP ; sw 0xb8(r6),r0   ; modo espejo XC = 0
GOP: movhi r6,0x3d ; lwz r6,0x5aa8(r6) ; beqi r6,0,FIN
lwz r6,0(r6) ; beqi r6,0,FIN ; sb 0xe(r6),r0 ; sb 0xf(r6),r0
FIN: j 0x25DA52                          ; continúa como el original
```

**Rutina 2** (envoltorio de la creación del contexto GOP):

```
addi r1,r1,-4 ; sw 0(r1),r9 ; jal 0x2AE8CB
movhi r6,0x3d ; lwz r6,0x5aa8(r6) ; beqi r6,0,E2 ; lwz r6,0(r6) ; beqi r6,0,E2
movi r5,1 ; sb 0xe(r6),r5 ; sb 0xf(r6),r5    ; OSD espejado por defecto
E2: lwz r9,0(r1) ; addi r1,r1,4 ; jr r9      ; r3 (resultado) intacto
```

- Las rutinas solo usan los registros r5–r7, que no se conservan entre llamadas, y comprueban cada puntero antes de usarlo.
- Las **13 lecturas del GOP vuelven a ser las originales**; ahora las banderas las pone la rutina 2 o la 1.
- Cada instrucción se generó con un ensamblador hecho a partir de las tablas oficiales y se verificó desensamblándola de vuelta.

## 6. Logo

- Es el recurso JPEG de la tabla TEL en flash `0x29B1B8` (hueco de 0x20138 bytes): 1366x768, baseline, 4:4:4, DRI 171.
- Se dibuja **antes** de que actúe el espejo. Por eso se guarda **girado 180°**, con las mismas tablas de cuantización, 4:4:4 y DRI 171.
- PSNR frente al original: 64.6 dB, sin diferencia visible. Ocupa 81 KB, se rellena con ceros y se conservan los 3 bytes finales del recurso.
- **Con MIRROR en OFF, el logo saldrá al revés**, porque el logo es una imagen fija y el arranque no lee la base de datos. Para un panel al derecho, regenera con `--sin-logo` o usa tu propio logo con `--logo archivo.jpg` (JPEG baseline 1366x768, como máximo 131 381 bytes).

## 7. Verificado y no verificado

**Verificado:**
- Contenedor y CRC.
- Solo cambian los bytes listados.
- Desensamblado del archivo final.
- Orden de arranque y ausencia de escritores del GOP.
- La opción Logo no tenía ningún otro uso.

**Falta probarlo en el TV:**
- ON igual que el firmware probado.
- OFF sin espejo en vídeo y menús, en todas las fuentes.
- Que el valor se mantenga tras apagar.

**Riesgo:** las rutinas se ejecutan desde la zona de datos de solo lectura de la imagen en RAM. Es memoria normal en esta CPU (sin MMU), pero no se ha probado. Si no arranca, graba `TV4-32K16_MIRROR.bin` (el probado) o el original.

## 8. Procedimiento de prueba

1. Graba `TV4-32K16_MIRROR_MENU.bin`: borrar → escribir → verificar.
2. Arranca: el logo debe salir **derecho** y la imagen y los menús, igual que con la versión anterior.
3. Menú de fábrica → "MIRROR" en **OFF** → apagar y encender → todo debe verse **al revés** en tu TV (es lo esperado con el panel invertido).
4. Vuelve a **ON** → apagar y encender → todo derecho otra vez.
5. Prueba también TV, AV, HDMI y USB, y cambia de canal.

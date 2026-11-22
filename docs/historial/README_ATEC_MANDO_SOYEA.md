# ATEC 32L18D — usar el mando del Soyea LKK32CMB

Análisis del 2026-11-15 (FirmReaper). Comparación de `Atec_32_KPC_Salva.BIN` (ATEC) con
`MST_EXT_SPI_ISP_20230301_153737.BIN` (Soyea, referencia). Direcciones = RAM (app en 0x243800 en ambos).

## Conclusión

El ATEC y el Soyea son el **mismo firmware de plataforma** (placa `BD_MST061B_10AZU_14014`, `KK_CUBA_SBOOT_20140718`):

| Elemento | ATEC | Soyea | ¿Igual? |
|---|---|---|---|
| SBOOT + PM (flash 0x00000–0x1FFFF) | — | — | **idénticos byte a byte** |
| Rutina del mando | `0x3233AA` | `0x32519F` | misma lógica |
| Tabla código → función (256 bytes) | `0x422090` | `0x423CE0` | **idéntica** (48 teclas) |
| Código de cliente aceptado | `0x7736` y `0x5FA0` | `0x7837` y `0x5FA0` | **solo cambia este** |

Por eso, para que el ATEC obedezca al mando Soyea basta **una instrucción**:

| Dirección | Original | Nuevo |
|---|---|---|
| `0x3233D5` | `c0eee6c0` `bg.sfeqi r7,0x7736` | `c0ef06e0` `bg.sfeqi r7,0x7837` (los mismos bytes que en el Soyea, `0x3251E3`) |

- Es la única instrucción de la app con el código de cliente como inmediato (búsqueda completa; las otras
  coincidencias del valor son destinos de salto). El PM no contiene el código de cliente.
- El código `0x5FA0` (otro mando aceptado por ambos firmwares) no se toca.
- Con el cambio, el mando **original del ATEC deja de funcionar** (se sustituye su código de cliente).

## Rutina (ATEC)

```
3233aa  movhi r23,0x5d ; lbz r7,-0x3cf8(r23) ; andi r7,r7,2 ; beqi r7,0,…     ¿hay código nuevo?
3233b8  lwz r7,-0x3cf4(r6)                                                    trama NEC recibida
3233c0  movhi r5,0x5555 ; ori r5,r5,0xaaaa ; sfne r7,r5 ; bnf …               (repetición)
3233ce  lwz r7,… ; exthz r7,r7
3233d5  sfeqi r7,0x7736 ; bf 0x323400                                          ← cliente del mando
3233dc  sfeqi r7,0x5fa0 ; bf 0x323400
323400  … comprueba código + ~código = 0xFF
32342c  movhi r5,0x42 ; addi r5,r5,0x2090 ; add r7,r5 ; lbz r7,0(r7)           función = tabla[código]
```

## No verificado

- **Probarlo en el TV.**
- **Encender desde reposo con el mando Soyea:** el PM es idéntico en los dos firmwares y no contiene el código de
  cliente; cómo configura el firmware el despertar por mando no está analizado. Si en el TV Soyea su mando
  enciende desde reposo, es probable que aquí también, pero **no lo sé**.

## En FirmReaper

Perfil `atec-32l18d-lp141wx1` v2, opción **Mando**: "ATEC original" (por defecto) / "Soyea LKK32CMB". Se combina
con la opción del panel (8/6 bits).

## Corrección del 2026-11-22: encender desde reposo

**Probado por el usuario** con el archivo de la v1.1.0 (`atec-32l18d-lp141wx1_bits-8_mando-soyea.BIN`,
SHA-256 `6c09f69b…440205e1a`): recién grabado, el mando Soyea funcionaba; tras apagar, **no volvía a encenderlo**.

**Causa.** Hay DOS sitios con el código de cliente:
1. `0x3233D5` (`bg.sfeqi`): lo compara el programa con el TV encendido (lee la trama completa). Este sí se cambiaba.
2. La inicialización del receptor IR por hardware (`0x3238F0…`): escribe el código byte a byte en los registros
   `0x3DA4` (`bn.ori r4,r0,0x36` en `0x32394C`) y `0x3DA5` (`bn.ori r4,r0,0x77` en `0x323957`). Ese filtro es el
   que usa el chip **en reposo** para decidir qué mando lo enciende. **No se cambiaba**, así que en reposo solo el
   mando ATEC (`36 77`) podía encenderlo.

El firmware del Soyea tiene la misma rutina con `0x37` / `0x78` (`0x3257A1` / `0x3257AC`). El análisis anterior
buscó el código solo como valor de 16 bits y por eso no vio estas dos cargas de 1 byte.

**Corrección (perfil ATEC v5).** "Mando = Soyea" cambia también esas dos instrucciones (`508036`→`508037`,
`508077`→`508078`), igual que el Soyea. El código de cliente elegido por el usuario también las cambia. Respecto al
archivo grabado solo cambian esos 2 bytes de la app. Nueva regla al cargar perfiles: una opción que toque el código
de cliente debe cambiar **todos** sus campos (principal y de reposo) al mismo código.

LG y Konka: su receptor IR toma el código de reposo del mismo campo que FirmReaper ya cambia (LG `0x2F6F35` lee
`0x39DEFE`; Konka `0x2A660F` lee `0x38FFA2`), así que no tienen este problema.

La 1.2.0 (publicada el 2026-11-18) también tenía el fallo; se corrige en la **1.2.1**.
Archivo corregido con las mismas opciones (8 bits + Soyea): SHA-256 `0b1bd2019841865cc875d8472eb9affb515d971dcbdc0759f35e2314915d0532`.

**Pendiente de probar:** el archivo corregido (encender desde reposo con el mando Soyea).

## Segunda corrección (2026-11-22, tarde): encender desde reposo — PRUEBA

**Probado por el usuario** con el archivo corregido (`0b1bd201…5d0532`): todo funciona con el mando Soyea, también
apagar, **salvo encender desde reposo**.

**Causa.** Además de la inicialización del receptor IR (`0x32394C`/`0x323957`), la rutina de **entrada en reposo**
(`0x2430F1`, texto `"=> StandBy"`) vuelve a escribir el código justo antes de apagar, fijo en dos instrucciones:

```
243115  bn.ori r7,r0,0x36 ; sb 0x7b48(r6),r7     RIU byte 0x7B48 = registro IR 0x3DA4, byte bajo
243120  bn.ori r7,r0,0x77 ; sb 0x7b49(r6),r7     RIU byte 0x7B49 = registro IR 0x3DA4, byte alto
24312b  movi r5,0xc ; sb 0x7b64/0x7b65/0x7b6c/0x7b6d   tecla POWER (0x0C, igual en ATEC y Soyea)
```

El Soyea tiene en el mismo sitio `0x37`/`0x78` (`50e037`/`50e078`). (En el análisis anterior esta pareja se vio
pero se descartó por error: se comparó mal con el Soyea.)

**Dónde está:** `0x243115` y `0x243120` quedan **antes** de la base de la app (`0x243800`), en los primeros 0x3800
bytes que van **sin comprimir** en la flash (flash `0x23195` y `0x231A0`), cubiertos por el CRC-32 de la cabecera.
FirmReaper aún no parchea esa zona (solo la app comprimida): se añadirá si la prueba funciona.

**Archivo de prueba:** `ATEC_8bit_mando_Soyea_PRUEBA_reposo.BIN`, SHA-256
`8e797d5dbb8056ec36630b3653eb194a35e08a515e86d404f6a4706ef92aaecd`. Respecto a `0b1bd201…`: flash `0x23197` `36→37`,
`0x231A2` `77→78` y el CRC de la cabecera (`0x7AC2E26B`). El análisis de FirmReaper lo da por válido.

LG, Konka y RCA leen el código de reposo del campo principal (LG `0x243124` ← `0x39DEFE`; Konka `0x2A557E` ←
`0x38FFA2`; RCA `0x2A6918` ← `0x398AB0`): no tienen este problema.

**Pendiente de probar:** encender desde reposo con el mando Soyea.

**Confirmado por el usuario (2026-11-22):** con `8e797d5d…` el mando Soyea funciona, apaga y **enciende desde
reposo**. Integrado en FirmReaper 1.3.0 (perfil ATEC v6): "Mando = Soyea" cambia los 5 sitios y el código de cliente
elegido también (4 campos de reposo). Para escribir en el código de arranque sin comprimir, el generador admite
ahora parches en flash `0x23080`–`0x23880` (ver `docs/PERFILES.md`). Con 1280x800 + 8 bits + Soyea, el programa
genera exactamente `8e797d5d…`.

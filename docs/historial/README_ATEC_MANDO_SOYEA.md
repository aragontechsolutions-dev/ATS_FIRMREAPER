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

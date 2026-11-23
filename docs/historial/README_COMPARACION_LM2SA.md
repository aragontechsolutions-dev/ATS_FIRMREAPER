# Comparación de dos firmwares de la placa PA20406-2-36ZG / LM2SA V1.6 (2026-11-23)

Archivos del usuario (fotos de la placa: "PA20406-2-36ZG COLD", "LM2SA V1.6"):

| | A: `MST_EXT_SPI_ISP_20210531_113958.BIN` | B: `MST_EXT_SPI_ISP_20210614_165953.BIN` |
|---|---|---|
| SHA-256 | `d0d2b24f4bda26564c62f7c8e1747dbdcddfc500b2ab73b796853be4913cf239` | `aa8b1502e7d88faa0c77f7cef86a5fbf739261846ebd94e02b891e53a978b421` |
| Contenedor MStar | válido (CRC `0x79F5BB5D`) | válido (CRC `0x8D0C9532`) |
| ID de placa / SBOOT | `BD_MST061B_10AZU_14014` / `KK_CUBA_SBOOT_20140718` | igual |
| Fecha de compilación del programa | **Apr 16 2015** | **May 22 2014** (más antiguo) |
| Programa descomprimido | 3 645 240 bytes | 3 554 952 bytes |

**No son iguales** (difieren 3 761 709 bytes de 4 194 304).
- Idénticos: SBOOT/PM (`0x0`–`0x20000`; el mismo que el ATEC y el Soyea) y 9 de los 11 recursos TEL, entre ellos
  el logo (`0300`), el sonido (`0701`) y `0312`/`0388`.
- Distintos: el programa (otra versión: A trae "Clone From TV to USB" y la librería DTMB interna "MD"; B trae la
  librería DTMB anterior con carga de firmware del demodulador), los recursos `0061` y `00B0`, y la zona de datos
  `0x3C0000`–fin (canales y ajustes guardados; distinta en los cuatro archivos comparados).
- Con el ATEC 32L18D (`Atec_32_KPC_Salva.BIN`) comparten el ID de placa, el SBOOT, la fecha de compilación de A
  (Apr 16 2015) y 7 recursos (A además tiene `00B0` igual que el ATEC). El logo y `0312`/`0388` son distintos.
- Ninguno de los dos está en el catálogo de FirmReaper: no se pueden parchear hasta analizarlos y crear su perfil.

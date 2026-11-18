# Logo de arranque del ATEC y del LG — análisis (2026-11-15)

Cómo se encontró dónde está el logo de cada TV y cómo lo carga su programa. Todo comprobado sobre los
originales `Atec_32_KPC_Salva.BIN` (`19491133…`) y `MST_EXT_SPI_ISP_20220511_165639.BIN` (`ef5c8055…`),
con el desensamblador de `tools/aeon` (barrido lineal de la app descomprimida; direcciones de RAM).

## 1. Recursos JPEG

| TV | Dónde | Imagen |
|---|---|---|
| ATEC | TEL `0x0300` en `0x2D5010`, 49 136 bytes | logo "atec" blanco sobre negro, 2167x1228, 4:2:0, reinicio 136; JPEG de 49 128 bytes + 6 ceros + CRC-16 |
| LG | TEL `0x0301` en `0x243B08` | **azul liso** 1366x768, 4:2:0 |
| LG | `0x370000` (fuera de la TEL) | "LG Life's Good" 1376x768, 4:2:2; JPEG de 27 753 bytes y `FF` hasta `0x3B0000` |

## 2. CRC-16 al final de cada recurso TEL

Probando variantes de CRC-16 sobre cada recurso menos sus 2 últimos bytes, la única que coincide en los 41
recursos de ATEC, LG, Soyea y Konka es **poly 0x8005, init 0, sin reflejar, sin XOR, big-endian**. El
`TV4-32K16_MIRROR_MENU.bin` (probado en el TV) tiene mal el del logo `0x065E`: el script Python no lo actualizó,
luego el Konka no lo comprueba. En el ATEC, la búsqueda de recursos `0x322A4A` solo recorre la tabla (devuelve
dirección, longitud redondeada a 8 y banderas): tampoco lo comprueba.

## 3. ATEC

```
2e350f: bg.ori r3,r0,0x300 ; bg.jal 0x2e34bc     → busca el recurso 0x0300 (0x24ed56 → 0x322a4a)
2e34ef: copia (longitud+7)&~7 bytes a RAM 0x1f30000 ; 2e352c: bg.jal 0x2b7550 (muestra el JPEG)
```
No hay otra fuente: el logo es siempre el recurso `0x0300`.

## 4. LG

- `msAPI_Flash_Init` (`0x26FF2A`): `s_Flash_u32WriteValidAddr_Start = 0x370000`, fin = tamaño − 1. Es la zona que
  el TV puede escribir. El mismo valor está en la cabecera TEL (`0x22088`), que en los demás firmwares es
  `0x3C0000`: ese campo es el **fin del área de recursos**.
- Función `0x2C238C`, llamada con `r3 = 0x301` desde `0x2C2428`:
  ```
  2c2395: lbz r7, RAM 0x53F565 ; beqi r7,2 → 2c23c0
     (≠ 2) 2c23a9: busca el recurso TEL 0x301 (imagen azul)
     (= 2) 2c23c0: r10 = ((lbz RAM 0x53ED79) << 14) & 0xC0000 ; r10 += 0x370000
           2c23ea: lee 0x40000 bytes de flash r10 a RAM 0x3110000 → JPEG
  ```
  Con el volcado del usuario, la zona es `0x370000` (en `0x3B0000` hay otros datos, no un JPEG).
- `0x2B8474`: `movhi r3,0x37 ; movhi r4,0x9` → desprotege `0x370000`+`0x90000` antes de escribir
  ("WriteProtect open fail"): es la grabación del "Capture Logo" del menú.

Conclusión: si el TV del usuario muestra "LG Life's Good" al encender, usa la zona `0x370000`.
**Confirmado por el usuario (2026-11-16): su LG muestra "LG Life's Good" al encender** → usa la zona `0x370000`
(el ajuste de logo vale 2). FirmReaper cambia
solo esa zona (hasta 0x40000 bytes, que es lo que lee el TV), con `FF` detrás.

## 5. Decodificación igual que Pillow

Al comparar el logo del LG girado con el de Pillow salió distinto. La causa: LibJpeg.NET (modelo libjpeg 7/8)
calcula la crominancia submuestreada con una IDCT escalada (16x8 / 16x16), mientras que libjpeg-turbo (API 6b)
la decodifica a 8x8 y la ensancha con un filtro triangular. Se corrigió (ver `docs/ARQUITECTURA.md`). Tras el
cambio, ATEC y LG girados = Pillow byte a byte, y el Konka sigue idéntico a la versión probada.

## Sin probar en el TV

Logos girados o propios del ATEC y del LG.

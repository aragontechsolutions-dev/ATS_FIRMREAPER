# Herramientas existentes y formato de firmware MStar (placas TV "no Android", SPI 4 MB, TSUMV59/V56/MSD3393, CPU AEON R2)

Fecha de la investigación: 2026-11-15. Restricciones de red de la sesión: no se pudo acceder a elektroda.com, cdn.hackaday.io ni (vía API) a algunos repos de GitHub, así que la evidencia de foros es solo lo que mostraban los resultados de búsqueda. La fuente primaria más fuerte es un SDK "Chakra3" de MStar filtrado en GitHub (xuzemin/mstar, snapshot `Chakra3_017a0d6_20170731`). Es exactamente la familia de middleware "non-OS" (NOS) de las placas V56/V59. Sus scripts de empaquetado coinciden con los offsets que observa el proyecto (0x20000 / 0x22080 / 0x23880).

---

## 1. ¿Qué herramientas públicas existen y qué hacen?

### Takeaway
No se encontró ninguna herramienta pública documentada que haga, sobre un dump SPI de 4 MB de una placa V56/V59 no Android, todo el ciclo: descomprimir la app, parchear el panel, el IR o el mirror, recomprimir y corregir el CRC. Las herramientas abiertas (mstar-bin-tool y similares) apuntan a `MstarUpgrade.bin` de TVs Android/eMMC o a monitores. Las comerciales (MstarTool) también apuntan a eMMC/Android. Los programadores (RT809H, CH341A con NeoProgrammer/AsProgrammer, flashrom) solo leen y escriben la flash. Hay un hueco real para la herramienta propuesta.

### Cited Findings
**Herramientas de (des)empaquetado de firmware MStar**
- **dipcore/mstar-bin-tool**: scripts Python de línea de comandos (`unpack.py`, `pack.py`, `extract_keys.py`, `secure_partition.py`) para `MstarUpgrade.bin`/`LetvUpgrade.bin`. Extrae las claves AES/RSA del MBOOT (offset por defecto 0x168e00, tamaño 0x450) y cifra/firma boot.img y recovery.img para SECURE_BOOT. Incluye `aescrypt2` para win32. Tiene unos 351 estrellas y 152 forks. — [GitHub dipcore/mstar-bin-tool](https://github.com/dipcore/mstar-bin-tool)
  - `unpack.py` asume una cabecera de 16 KB con un script de MBOOT (`filepartload`, `mmc`, `store_secure_info`, `store_nuttx_config`) y soporta `unlzo` y `unlzo.continue` (LZO). No maneja dumps SPI crudos, deflate ni el CRC de un chunk header (lectura del código: `HEADER_SIZE = 16 * utils.KB`). — [unpack.py](https://raw.githubusercontent.com/dipcore/mstar-bin-tool/master/unpack.py)
  - No se encontró ningún archivo LICENSE en el repo (comprobado en raw). La página de GitHub tampoco muestra licencia. — [GitHub](https://github.com/dipcore/mstar-bin-tool)
  - Hay forks y mirrors: sha-man-4pda/mstar-bin-tool y gitee backbus/mstar-bin-tool. — [GitHub sha-man-4pda](https://github.com/sha-man-4pda/mstar-bin-tool), [Gitee](https://gitee.com/backbus/mstar-bin-tool)
- **cosmicdan/Mstar_bintool**: scripts de unpack/repack para `MstarUpgrade.bin`. GPL-2.0, último push en 2016. — [GitHub](https://github.com/cosmicdan/Mstar_bintool) (metadatos vía API de búsqueda de GitHub)
- **skotopes/mstarutils**: `MstarUtil.py validate|write`. Parsea el script de upgrade, que termina en `% <-this is end of file symbol`, y escribe a eMMC vía SDIO (Raspberry Pi). Solo eMMC y Linux. — [ReadMe](https://raw.githubusercontent.com/skotopes/mstarutils/master/ReadMe.md)
- **shinyquagsire23/mstar_extract_decompress**: "Extracting and decompressing MSTAR monitor firmwares", en C, último push en 2023-02. Es el más parecido a nuestro caso:
  - busca la tabla `BIN_PACKAGE_HEADER_ID0 = 0x54454C09` ("TEL\t") alineada a 0x80;
  - lista las entradas (ID, dirección y longitud en BE, flags);
  - descomprime las que tienen el flag `& 4` con `MsDecompress` (zlib `inflateInit2(-MAX_WBITS)`, o sea deflate crudo);
  - extrae la app principal a partir de `archive_start+0x800`, saltando ceros cada 0x80 bytes.
  - No recomprime ni corrige el CRC. No se encontró archivo de licencia; el `ms_decompress.c` incluido viene de MediaTek, "GPL-2.0-only OR BSD-3-Clause". — [main_extract.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/main_extract.c), [ms_decompress.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/ms_decompress.c)
- **linux-chenxing/mstarfwcutter**: corta dumps SPI NOR de SoCs MStar/SigmaStar nuevos según los registros de partición `MXPT`…`TPXM`. GPL-3.0. No aplica a placas V59 con tabla TEL. — [mstarfwcutter.py](https://raw.githubusercontent.com/linux-chenxing/mstarfwcutter/master/mstarfwcutter.py)
- **YoelCode-ui/MstarTool** (comercial, v3.3.0, Windows 10/11 x64):
  - procesa particiones eMMC (EXT4) y hace dd de MMC;
  - "Gestión de Logos" (JPG/PNG);
  - "Configuración de Panel / Mirror" (resolución, VESA/JEIDA, profundidad de bits, LVDS y dual port, capas OSD/Video en espejo, biblioteca de perfiles de panel);
  - control remoto mediante búsqueda y reemplazo de *archivos* de configuración;
  - licencia por hardware y protección anti-manipulación.
  - Está orientada a Smart TV con eMMC y sistema de archivos. No menciona SPI de 4 MB, deflate ni CRC. — [README MstarTool](https://raw.githubusercontent.com/YoelCode-ui/MstarTool/HEAD/README.md)
- **shrinathpol/mstar-bin-tool** (2026): a pesar del nombre, trabaja sobre HiSilicon Hi3751V350 (`allupgrade_V351_sos.bin`):
  - recalcula los CRC de partición;
  - parchea `key.xml` (códigos IR NEC del tipo `0xbb447f00`);
  - genera el logo;
  - cambia la PWM del backlight de 180 Hz a 25 kHz.
  - Sirve como referencia de "equivalente Hisense/HiSilicon". — [README](https://raw.githubusercontent.com/shrinathpol/mstar-bin-tool/HEAD/README.md)
- **ankit-ux/Custom-AOSP-TV-Firmware-Modification-Toolkit**: se describe como multiplataforma (Amlogic, MediaTek, MStar, Rockchip, Hisense) para logos, animaciones y remotos de firmware AOSP/Android TV. Solo se vio la descripción; no está verificado. — (resultado de búsqueda de repos de GitHub, `gh search`)

**Programadores y lectura/escritura de flash**
- **flashrom `mstarddc_spi`**: encapsula SPI sobre el I2C del DDC (VGA/DVI). El puerto ISP suele estar en 0x49 (EDID en 0x50, DDC/CI en 0x37). Se usa con `flashrom -p mstarddc_spi:dev=/dev/i2c-X:49`. El monitor tiene que estar en standby y conectado a una salida VGA inactiva. En su momento los mantenedores lo deshabilitaron por defecto por dudas de calidad. GPL-2.0. — [flashrom ML 2014](https://www.flashrom.org/pipermail/flashrom/2014-May/012348.html), [commit r1860](https://www.flashrom.org/pipermail/flashrom/2014-December/013090.html), [flashrom ML 2015](https://www.flashrom.org/pipermail/flashrom/2015-February/013359.html)
- **memues/mstar-monitor-firmware-dumper** (2026): script para Linux que compila flashrom con `mstarddc_spi`, entra en modo ISP enviando la cadena mágica `"MSTAR"` por I2C (dirección 0x49), lee la flash dos veces y compara los MD5. Solo lee. — [README](https://raw.githubusercontent.com/memues/mstar-monitor-firmware-dumper/HEAD/README.md)
- **MStar ISP Tool / "ISP_Tool_4528"** (Windows, propietaria, de MStar): los manuales de servicio la describen como "Open Mstar 'Mboot' software programming tool 'ISP_Tool_4528'": configurar, leer el binario SpiFlash/mboot y conectar al TV por el puerto VGA/DDC. No hay documentación oficial pública. — [Manual Scott TX32 (manualslib)](https://www.manualslib.com/manual/2532765/Scott-Tx32.html?page=56); la versión 4528 viene del resumen de búsqueda sobre [RCA 55AN3D](https://www.manualslib.com/manual/1954450/Rca-55an3d.html?page=24)
- **RT809H / RT809F** (iFix, Windows, propietario): lee y escribe placas de TV/monitor "sin desmontar" por VGA ISP y por HDMI, además de ISP por cable y lectura offline de SPI 24/25/93/95 y NOR paralelo. El flujo es autoidentificar, leer, guardar backup y escribir. Hay casos reportados de placas que no se pueden leer por VGA/HDMI o que requieren un cable VGA-VGA concreto. No se encontró que edite el contenido (logo/panel). — [Manual iFix RT809](https://www.manualslib.com/manual/1499705/Ifix-Rt809-Series.html), [jestineyong.com](https://jestineyong.com/easy-tv-and-monitor-boards-firmware-programming-with-the-rt809h-programmer/)
- **AsProgrammer (nofeletru/UsbAsp-flash)**: SPI, I2C y MicroWire con CH341a, CH347, UsbAsp, AVRISP, Arduino y FT232H. Licencia MIT, activo en 2026-08. — (metadatos vía API de GitHub: [repo](https://github.com/nofeletru/UsbAsp-flash))
- **NeoProgrammer** (freeware, cerrado): CH341A vía WinUSB (driver instalado con Zadig); hace verify, borrado completo y blank check. No hay ficha oficial verificable. — [manual CH341A ShillehTek](https://shillehtek.com/blogs/shillehtek-product-manuals/ch341a-usb-eeprom-bios-programmer-manual), [winraid guía CH341A](https://winraid.level1techs.com/t/guide-using-ch341a-based-programmer-to-flash-spi-eeprom/30834?page=20)
- **binwalk** (ReFirmLabs, MIT) y **firmware-mod-kit** (rampageX, export de Google Code, sin licencia declarada en los metadatos): son genéricos. Ninguno conoce la tabla TEL, el chunk header de 0x80 ni el layout "0x3800 bytes sin comprimir + deflate" de MStar. — (metadatos vía API de GitHub: [binwalk](https://github.com/ReFirmLabs/binwalk), [firmware-mod-kit](https://github.com/rampageX/firmware-mod-kit))

**Herramientas chinas "V59/V56 logo/panel"**
- Las búsquedas (inglés y chino: "V59 V56 主板 改屏参 改开机logo 工具") solo devolvieron mstar-bin-tool y páginas SEO basura (edx.ipeindia.org) con nombres como "mstar bin tool guiv232", "Itv.v59.031 software" o "tp vst59 p67 software". Su contenido no es fiable ni verificable. — [búsqueda; ej. página SEO](https://edx.ipeindia.org/atlas-lyric/glade-nest/mstar-bin-tool-guiv232-download-link)
- En Elektroda hay hilos con dumps SPI de TP.VST59S.PB813, peticiones de firmware con flip/mirror y casos de imagen invertida en V59 (Hisense LEDN32D33 con panel JHD315GH-E52). No se pudo leer su contenido porque el dominio está bloqueado en la sesión. — [topic3859739](https://www.elektroda.com/rtvforum/topic3859739.html), [topic3159435](https://www.elektroda.com/rtvforum/topic3159435.html), [topic4174233](https://www.elektroda.com/rtvforum/topic4174233.html)

### Inferences
- La práctica habitual de los técnicos parece ser "buscar un dump ya hecho para la combinación placa+panel+resolución+mirror y flashearlo con RT809H o CH341A", no editar el binario. Una herramienta que edite PanelType, IR y mirror y regenere los CRC sobre el dump propio superaría lo que hay en público.
- Funciones mínimas para igualar o superar al resto:
  - lectura y verificación de dump (doble lectura con MD5, como hace memues);
  - parseo de la tabla TEL;
  - descompresión y recompresión de la app (deflate y también LZMA y LZSS, por si otras variantes usan los flags 0x08 o 0x01);
  - editor de PanelType con perfiles;
  - cambio de logo (recurso JPEG en la TEL);
  - remapeo del IR (código de cliente y tabla);
  - recálculo de CRC32 del chunk, CRC16 por recurso y los CRC finales de la imagen;
  - diff y backup automático.

### Gaps
- No se pudo verificar ninguna herramienta china concreta ("VST59 logo tool", "MStar Panel Tool") con fuente primaria. Tampoco si RT809H incluye editor de logo o panel (su software tiene módulos, pero no se halló documentación al respecto).
- Las licencias de dipcore y shinyquagsire23 no están declaradas: no hay archivo LICENSE.

---

## 2. Layout público de la imagen SPI MStar (SBOOT/MBOOT, MSIF, chunk header, CRC, compresión, TEL, bancos DB)

### Takeaway
El SDK Chakra3 filtrado documenta, a través de sus scripts de build, el layout que se observa en el dump:
- SBOOT de 64 KB en 0x0;
- código PM de 64 KB en 0x10000;
- **chunk header de 0x80 bytes en 0x20000**, con el CRC32 de la app en +0x14;
- la app empieza en 0x20080, con sus **primeros 0x3800 (14336) bytes sin comprimir** (de ahí 0x20080+0x3800 = **0x23880**) y el resto en deflate crudo (`mscompress -c -u 14336 -9`) o en LZMA (`mscompress7`);
- la tabla de recursos "TEL" está dentro de esa parte sin comprimir;
- cada recurso lleva relleno y un CRC16 al final;
- los bancos de base de datos están al final de la flash.

### Cited Findings
**Constantes del layout (scripts del SDK Chakra3)**
- `BOOTLOADER_SIZE = 0x10000  # The size of sboot is 64K bytes`, `PM_CODE_SIZE = 0x10000`, `CHUNK_HEADER_SIZE = 0x80`, `UNCOMPRESSED_LEN = 0x3800  # The size of uncompressed part is 14K bytes`. — [BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py)

**CRC32 de la app**
- `CRC32 = crcmod.mkCrcFun(0x104C11DB7, 0, 0)`: polinomio 0x04C11DB7, init 0, no reflejado (`rev=False`), xorOut 0. Coincide exactamente con lo observado.
- Se calcula sobre `len_of_AP` bytes (el tamaño de AP_C.bin, es decir, la parte sin comprimir más el deflate) a partir de `BOOTLOADER_SIZE + PM_CODE_SIZE + CHUNK_HEADER_SIZE` (= 0x20080).
- Se escribe en little-endian en `0x20000 + 0x14`.
- En `0x20000 + 0x1C` se escribe `file_size` (tail+4). En la variante sin compresión, `PadLoaderCrc.py` escribe en `+0x18` el offset del AP alineado. — [BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py), [PadLoaderCrc.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/PadLoaderCrc.py)

**Otros CRC de la imagen**
- `CRC16 = crcmod.mkCrcFun(0x18005, 0, 0)`: polinomio 0x8005, init 0, no reflejado (equivale a CRC-16/UMTS, también llamado BUYPASS).
- CRC16 global: se calcula desde `BOOTLOADER_SIZE` (0x10000) hasta el final de la imagen menos 2 y se escribe en big-endian en los 2 últimos bytes de la imagen.
- Al final el script añade además el CRC32 (BE) de todo el archivo (`f.seek(0); crc = CRC32(f.read()); f.write(crc)`). — mismas fuentes

**Compresión de la app** (comandos del Makefile)
- `mscompress -c -u 14336 -9 AP.bin > AP_C.bin` cuando `BIN_FORMAT=COMPRESS` (deflate con 14336 bytes sin comprimir).
- `mscompress7 e 14336 AP.bin AP_C.bin` cuando `BIN_FORMAT=COMPRESS7` (LZMA).
- Si no, la app va sin comprimir.
- Existe también LZSS (comentado). — [Makefile Chakra3](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/Makefile)

**Descompresión en el lado del firmware**
- `MsDecompress` usa zlib `inflateInit2(&z, -MAX_WBITS)` (deflate crudo).
- `DoMsDecompression` lee la **longitud original como u32 LE en los últimos 4 bytes** del stream comprimido y falla si la longitud de salida no coincide.
- `DoMsDecompression7` es LZMA con cabecera estándar (5 bytes de props y 8 bytes de tamaño). — [ms_decompress.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/ms_decompress.c)

**MS_IMG_INFO** (sección `.prog_img_info`, en `IMG_INFO_OFFSET` = dirección de la sección + 0x10000)
- `CompressMagicNum` (3 bytes LE) está en +0x47: 0xDEADBE si `compress_format=='0'`, 0xDEADEF en caso contrario.
- La longitud de AP_C (u32 LE) está en +0x4A.
- Estructura `MS_IMG_INFO`:
  - `u32Magic` (MAGIC_APP = 0x55AA5678, MAGIC_BOOTLOADER = 0x55AA1234);
  - bank/offset de pack info;
  - `u32Length`, `u32OUI`, `Selector_Byte[4]`;
  - `HW_Model`/`HW_Version`, `SW_Model`/`SW_Version`, `BoardType`;
  - `u8ModelName[20]`, `u8PanelName[20]`, `u8PanelType`;
  - `CompressMagicNum[3]`, `u32CompressedLength`;
  - `u32MagicId_2` = 0x55AAABCD.
  - Los offsets cuadran: PanelType en 0x46, magic de compresión en 0x47, longitud en 0x4A. — [BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py), [Makefile (IMG_INFO_OFFSET)](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/Makefile), [main_extract.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/main_extract.c)

**Tabla TEL** (`BinaryInfoBlock[]`, sección `.img_info`)
- Formato:
  - `BIN_PACKAGE_HEADER_ID0 = 0x54454C09` ("TEL\x09") y `ID1 = 0x58336900` ("X3i\0");
  - u32 BE con el tamaño de flash reservado (`FMAP_TOTAL_USE_BANK_END*FLASH_BLOCK_SIZE`);
  - u16 BE con el número de bins, que rellena la herramienta externa;
  - entradas de 11 bytes: `B_ID` u16, `B_FAddr` u32, `B_Len` u32 (todos BE) y `B_IsComp` u8.
- La tabla termina con la entrada ID 0xF000 (`BIN_ID_MAGIC_55AAABCD`), que el bootloader verifica.
- Las entradas vacías en el binario compilado son `0xCCCCCCCC`/`0xCC`. — [InfoBlock.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/dvbt/InfoBlock.c), [BinInfo.h milan](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/bininfo/milan/BinInfo.h)
- El empaquetador busca el magic de 5 bytes `0958336900` (es decir, `\x09X3i\x00`), lee el u32 y el u16 y recorre las entradas de 11 bytes. Para cada recurso:
  - lo comprime según el flag;
  - lo rellena con ceros hasta que `(len+2) % 8 == 0`;
  - le **añade un CRC16 en BE**;
  - escribe la dirección y la longitud (la longitud incluye el relleno y el CRC16) y el flag.
- Flags: `0x01` LZSS (`-c`), `0x04` MSCOMPRESS/deflate (`-g`), `0x08` MSCOMPRESS7/LZMA (`-7`), `0x02` / `0x80` Flash2. — [BinIDPackFiles_Compress.py](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/BinIDPackFiles_Compress.py)
- shinyquagsire23 deduce `appimg_start = archive_start - 0x2000`, es decir, que la TEL está en app+0x2000. Con la app en 0x20080, eso da TEL = 0x22080, que coincide con el dump del proyecto. — [main_extract.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/main_extract.c)
- Otras cabeceras de recursos: `BIN_DISPLAY_HEADER` 0x54454C09/0x31207341, `BIN_DRAMMAP_HEADER` 0x4D5354FA/0x50417461, `BIN_IRMAP_HEADER_ID` 0x77554321, `BIN_KEYPAD_HEADER_ID` 0x4368656E. — [BinInfo.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/bininfo/milan/BinInfo.h)

**IDs de recursos relevantes** (BinInfo.h milan; el número de ID puede variar por chip o proyecto)
- `BIN_ID_JPEG_BOOT_LOGO_DVB 0x0300` (logo de arranque **JPEG**, ~50 KB, `Mstar_OSD1_480p.jpg`)
- `BIN_ID_MELODY_ON_DVB 0x0701` (mp3 de encendido)
- `BIN_ID_PANEL 0x0747` (Panel.Bin, ~2 KB)
- `BIN_ID_OSDCP_BMP_*` (0x0301/0x0311/0x0322, bitmaps del OSD)
- `BIN_ID_OSDCP_TEXT_*` (0x0380…, cadenas)
- `BIN_ID_CODE_AUDIO_DEC_R2_COMP 0x0003`, `BIN_ID_CODE_VDEC_FWVPU_COMPRESS 0x00B1`
- fuentes 0x01A0…
- `BIN_ID_ISP_UPDATE_FLAG 0xE000`, `BIN_ID_BINMANAGER_KEY 0xEFFF`, `BIN_ID_MAGIC_55AAABCD 0xF000`
- En InfoBlock.c aparecen también `BIN_ID_PANEL_BIN` y `BIN_ID_IR_MSTAR`. — [BinInfo.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/bininfo/milan/BinInfo.h), [InfoBlock.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/dvbt/InfoBlock.c)

**Bancos de base de datos** (`MApp_FlashMap.h`, contando hacia atrás desde el último banco)
- `FMAP_GEN_SETTING_BANK` (2 bancos, al final) → `CH_DB` (2×DB_BANK_COUNT) → `FACTORY_SETTING` (1, opcional) → `TCON_BIN` → `HDCP_KEY` → `VERSION_DB` → `OAD` → `CI` → `CI_PLUS` → `CANALREADY_AUTH` → `CAPTURE_LOGO` (8 bancos, opcional).
- `FMAP_TOTAL_USE_BANK_END` = inicio de CAPTURE_LOGO, y es el valor que se guarda en la cabecera TEL.
- Un comentario indica `HDCP_DB_BANK` "7B(8MB) 3B(4MB)". — [MApp_FlashMap.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/common/app/include/MApp_FlashMap.h)

**MSIF / MBOT**
- `MSIF_TAG {'M','S','I','F'}` + `MSIF_CLASS {'0','0'}` + CUS/MOD/CHIP/CPU/OS son las etiquetas de versión de cada librería.
- El script de versiones extrae 16 caracteres tras "MSIF" con el formato `XXXX-XX.XX.XXXXXXXX`.
- Las versiones del bootloader se detectan con `strings | grep "MBOT-"`.
- Así, "MSIF0000S3 MBOT0206" son etiquetas de versión, no estructuras de partición. — [MsVersion.h](https://raw.githubusercontent.com/neuschaefer/mstar-mboot/master/MstarCore/bsp/k2/include/MsVersion.h), [Version_Control.pl](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/scripts/version/Version_Control.pl)

**Variantes del SDK**
- Chakra3 tiene BSPs `maya_nos_r2m`, `milan_nos_mips`, `marlon_nos_mips`, `nasa_nos_r2m`, `melody_nos_r2m`, `whisky_nos_r2m` ("nos" = no OS; "r2m" = AEON R2).
- Contiene código `EANBLE_V56_IO_KEY` (autor "xzm", 2017), lo que indica uso en placas V56. — [MApp_IR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_IR.c); lista de BSP vía búsqueda de código en GitHub.

### Inferences
- Los 6 u32 en 0x20000 que observa el proyecto (inicio en flash, destino en RAM, fin en RAM, fin en flash, 0, CRC) coinciden con el chunk header del SDK: el CRC32 en +0x14 es sobre AP_C. El rango del CRC es probablemente desde 0x20080 hasta "fin en flash", y conviene contrastarlo con `len_of_AP` o `file_size` en +0x1C.
- **La TEL (0x22080) está dentro de la parte sin comprimir cubierta por el CRC32.** Cualquier cambio en la TEL (por ejemplo, reubicar el logo) obliga a recalcular el CRC32 del chunk.
- Recomprimir deflate con otro compresor (zlib, zopfli) debería ser aceptable si:
  - se mantiene el stream crudo;
  - se mantiene el **u32 LE final con el tamaño descomprimido**, si el bootloader usa la variante `DoMsDecompression`;
  - se actualiza el "fin en flash";
  - el tamaño no invade el primer recurso de la TEL.
  - Esto es inferencia y hay que verificarlo en el dump.
- Puede haber un CRC16 global y un CRC32 global al final de la imagen. No consta si el SBOOT los verifica en el arranque o si solo los usa el ISP o el upgrade por USB. La herramienta debería detectarlos y recalcularlos si existen.
- El logo de arranque probablemente es un recurso JPEG en la TEL (ID 0x0300 o similar) con un CRC16 añadido. Cambiar el logo = reemplazar el JPEG (respetando tamaño, alineación y resolución del decodificador) y regenerar el CRC16 y la entrada de la TEL.

### Gaps
- No se encontró documentación de qué valida exactamente el SBOOT de TSUMV59/V56 (el CRC32 del chunk sí; el CRC16 global, los CRC16 por recurso y el 0x55AAABCD, no consta).
- El mapeo de códigos internos a chips comerciales (maya/milan/marlon frente a TSUMV59/TSUMV56/MSD3393) no está verificado.
- La cadena "KK_CUBA_SBOOT" no aparece en las fuentes públicas encontradas (búsqueda de código en GitHub sin resultados). Probablemente sea un identificador del fabricante de la placa o del cliente.
- No se pudo leer el PDF "Introduction to MBoot" (hackaday) porque el dominio está bloqueado.

---

## 3. Estructura PanelType (MApi_PNL) y enums

### Takeaway
El struct `PanelType` y sus enums son públicos a través de cabeceras filtradas en GitHub: `apiPNL.h` de kogan-tv-gpl/MBoot, de neuschaefer/mstar-mboot y del propio Chakra3 para cada BSP. Los valores son `TI_10BIT_MODE=0`, `TI_8BIT_MODE=2`, `TI_6BIT_MODE=3` y `OUTPUT_10BIT_MODE=0`, `OUTPUT_6BIT_MODE=1`, `OUTPUT_8BIT_MODE=2`. **Ojo:** los anchos de campo varían entre BSPs. Por ejemplo, `m_ucPanelHSyncBackPorch` es U8 en edison y U16 en maya_nos_r2m, así que el layout binario hay que determinarlo por chip y validarlo en el dump.

### Cited Findings
- Struct en orden:
  - `const char *m_pPanelName`;
  - `m_bPanelDither:1`, `APIPNL_LINK_TYPE m_ePanelLinkType:4`, … (bitfields de swap: `m_bPanelDualPort`, `SwapPort`, `SwapOdd_ML`/`Even_ML`, `Odd_RB`/`Even_RB`, `SwapLVDS_POL`, `SwapLVDS_CH`, `PDP10BIT`, `LVDS_TI_MODE`);
  - `m_ucPanelDCLKDelay`, bitfields `InvDCLK`/`InvDE`/`InvHSync`/`InvVSync`;
  - 4 corrientes de salida (0=4 mA, 1=6 mA, 2=8 mA, 3=12 mA);
  - `m_wPanelOnTiming1/2` y `OffTiming1/2` (U16);
  - `HSyncWidth`, `HSyncBackPorch`, `VSyncWidth`, `VBackPorch`;
  - `m_wPanelHStart`, `VStart`, `Width`, `Height`;
  - `Max`/`HTotal`/`Min` H y V (U16);
  - `m_dwPanelMaxDCLK`/`DCLK`/`MinDCLK` (declarados **MS_U8**);
  - `m_wSpreadSpectrumStep`/`Span`;
  - `m_ucDimmingCtl`/`MaxPWMVal`/`MinPWMVal`;
  - `m_bPanelDeinterMode:1`, `E_PNL_ASPECT_RATIO m_ucPanelAspectRatio`;
  - `MS_U16 m_u16LVDSTxSwapValue`;
  - `APIPNL_TIBITMODE m_ucTiBitMode` ("MOD_4B[1:0]"), `APIPNL_OUTPUTFORMAT_BITMODE m_ucOutputFormatBitMode`;
  - swaps RG/GB;
  - `m_bPanelDoubleClk:1`, `MS_U32 m_dwPanelMaxSET`, `MS_U32 m_dwPanelMinSET`;
  - `APIPNL_OUT_TIMING_MODE m_ucOutTimingMode`, `m_bPanelNoiseDith:1`. — [apiPNL.h (kogan-tv-gpl, edison)](https://raw.githubusercontent.com/Benjamin-Dobell/kogan-tv-gpl/master/MBoot/MstarCore/bsp/edison/include/apiPNL.h)
- `APIPNL_LINK_TYPE`: LINK_TTL=0, LVDS=1, RSDS=2, MINILVDS=3, ANALOG_MINILVDS=4, DIGITAL_MINILVDS=5, MFC=6, DAC_I=7, DAC_P=8, PDPLVDS=9, EXT=10.
- `APIPNL_LINK_EXT_TYPE` continúa desde 10 (EPI34_8P=10 … MINILVDS_*, HS_LVDS=21, HF_LVDS=22, TTL_TCON=23, …). — misma fuente
- `APIPNL_TIBITMODE`: TI_10BIT_MODE=0, TI_8BIT_MODE=2, TI_6BIT_MODE=3. `APIPNL_OUTPUTFORMAT_BITMODE`: OUTPUT_10BIT_MODE=0, OUTPUT_6BIT_MODE=1, OUTPUT_8BIT_MODE=2. `APIPNL_OUT_TIMING_MODE`: E_PNL_CHG_DCLK=0, HTOTAL=1, VTOTAL=2. — misma fuente
- En Chakra3 `maya_nos_r2m/include/apiPNL.h` el struct es igual salvo `MS_U16 m_ucPanelHSyncBackPorch` (en edison es MS_U8). — [apiPNL.h maya](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/core/drv_bsp/maya_nos_r2m/include/apiPNL.h) (comparado con diff)
- Hay más copias: neuschaefer/mstar-mboot `MstarCore/bsp/k1/include/apiPNL.h`, czc0713/Mst786 `drivers/mstar/pnl/apiPNL.h`, y SigmaStar (wireless-tag-com/openwrt-ssd20x, industio/PurPle-Pi-R1) con archivos de panel `*.h`/`.ini` que usan `m_ucTiBitMode`. — búsqueda de código en GitHub (`gh search code m_ucTiBitMode`)
- `kogan-tv-gpl/MBoot/MstarCore/src/drivers/panel/panel.c` y `MsApiPanel.c` usan estos campos (los tablas de panel cargadas desde la DB del MBoot Android). — búsqueda de código en GitHub
- En Chakra3 hay además un recurso `BIN_ID_PANEL 0x0747` (Panel.Bin, ~2 KB) en la TEL. En algunos builds, los parámetros de panel pueden estar en ese recurso y no solo dentro de la app. — [BinInfo.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/bininfo/milan/BinInfo.h)
- Mirror: en Chakra3 se controla con el flag de compilación `MirrorEnable` y en tiempo de ejecución con `MApp_Image_GetMirror()`/`MApp_Image_SetMirror()` (`MApp_PreInit_Image_SetMirror`). Hay ajustes de UI para paneles mirror (`FIXED_MIRROR_HD_PANEL_UI_LOSE`), y la API del XC expone `MApi_XC_EnableMirrorModeEx`. — [MApp_Init.c (búsqueda)](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_Init.c)

### Inferences
- Los bitfields y los enums dentro del struct dependen del compilador AEON (gcc para or32/R2). El tamaño de un enum suele ser de 4 bytes salvo `-fshort-enums`. Lo prudente es detectar el layout por heurística en el dump:
  - localizar los punteros `m_pPanelName` (direcciones de RAM dentro del rango de la app que apuntan a cadenas tipo "PNL_…", "HV320WXC…");
  - inferir el stride de la tabla por la distancia entre punteros consecutivos;
  - validar con valores plausibles (Width 1366/1920, Height 768/1080, HTotal > Width).
- `m_dwPanelMaxSET` y `MinSET` son U32 y, junto con HTotal y VTotal, son los que los foros chinos llaman "maxset/minset/PLL SET" (en los resultados de búsqueda en chino aparecían como parámetros de 屏参, sin verificar).
- El mirror en placas no Android probablemente sea un valor de fábrica guardado en la DB del final de la flash (banco de factory o general setting) o un flag compilado en la app. La herramienta debería contemplar ambos casos.

### Gaps
- No se encontró el layout exacto (offsets) de PanelType compilado para TSUMV59/V56. Los BSP maya/milan sirven de guía pero no están verificados contra un binario.
- No se encontró el formato de `Panel.Bin` (BIN_ID_PANEL).

---

## 4. ¿Cómo se almacenan el decodificado IR y los keymaps?

### Takeaway
En el SDK de MStar el código de cliente NEC se define como `IR_HEADER_CODE0/1`, por ejemplo 0x80/0x7F en el remoto MStar DTV de referencia. El driver lo escribe en los registros `IR_CCODE_L/H` del decodificador por hardware ("full decode") o lo compara por software. Las teclas son `#define IRKEY_xxx <código>` en una cabecera `IR_*.h` por modelo de remoto, compiladas en `switch`/comparaciones dentro de la app; no son necesariamente una tabla de datos. Existe un mecanismo opcional `ENABLE_IR_BIN` que carga un keymap remapeable desde un recurso TEL (`BIN_ID_IR_MSTAR`).

### Cited Findings
- Referencia `IR_MSTAR_DTV.h`:
  - `IR_HEADER_CODE0 0x80` (Custom 0), `IR_HEADER_CODE1 0x7F` (Custom 1);
  - tiempos NEC: cabecera de 9000 µs, off de 4500 µs, repetición de 2500 µs, lógico 01H de 560 µs, 0 de 1120 µs, 1 de 2240 µs, timeout de 140000 µs;
  - tabla Linux `{0x0046, KEY_POWER}, {0x0050, KEY_0}, {0x0049, KEY_1}, {0x0055, KEY_2}, … {0x0052, KEY_UP}, {0x0013, KEY_DOWN}, {0x0006, KEY_LEFT}, {0x001A, KEY_RIGHT}`. — [IR_MSTAR_DTV.h (PurPle-Pi-R1)](https://raw.githubusercontent.com/industio/PurPle-Pi-R1/master/kernel/drivers/sstar/ir/include/IR_MSTAR_DTV.h)
- Driver Chakra3 `msIR.c` (maya):
  - modos `IR_MODE_SEL` = FULLDECODE, RAWDATA, SWDECODE_NEC, SWDECODE_KON, SWDECODE_RC5, SWDECODE_SHA, SWFIFO, HWDECODE_RC;
  - escribe `msIR_WriteByte(IR_CCODE_L, IR_HEADER_CODE0); msIR_WriteByte(IR_CCODE_H, IR_HEADER_CODE1);`;
  - programa los límites del tiempo de cabecera;
  - en modo raw compara `u8IRRawModeBuf[0] == IR_HEADER_CODE0 && [1] == IR_HEADER_CODE1`. — [msIR.c maya](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/device/ir/maya/msIR.c)
- `MApp_IR.c`: el mapeo de `IRKEY_*` a `KEY_*` se hace con `switch` (`case IRKEY_POWER:`, `case IRKEY_NUM_0: u8KeyCode = KEY_0;` …) condicionado por `IR_TYPE_SEL` (por ejemplo `IR_TYPE_MSTAR_DTV`). — [MApp_IR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_IR.c)
- Formato "IR Bin" (`ENABLE_IR_BIN`, recurso `BIN_ID_IR_MSTAR`):
  - cabecera de 32 bytes (`IR_BIN_HEADER 32`);
  - bytes [2] y [3] = código de cliente; si son 0 se usan los valores por defecto compilados;
  - checksum u32 LE en [28..31] = suma de los bytes desde el offset 32 hasta `BinDataSize`, donde `B_Len-2` se redondea hacia abajo a múltiplo de 8 (hay relleno y CRC16 al final);
  - registros de 3 bytes `(cmd, data1, data2)`: `IRBIN_KEYMAP_CODE` → `g_u16IRkeyMap[data1] = data2 | 0x1000`; `IRBIN_BIN_END` termina.
  - `MApp_IrBin_GetKeyCode` busca en `g_u16IRkeyMap` (tamaño `IRBIN_NUM`). — [MApp_IR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_IR.c)
- Kogan MBoot (`apiIR.c`) permite sobrescribir el código de cabecera desde variables de entorno (`getenv(IR_HEADER_CODE0_NAME)`). Aplica a MBoot Android, no a NOS. — [apiIR.c](https://raw.githubusercontent.com/Benjamin-Dobell/kogan-tv-gpl/master/MBoot/MstarCore/src/api/apiIR.c)
- Equivalente en HiSilicon/Hisense: keymap en `key.xml` con códigos NEC completos de 32 bits (ej. `0xbb447f00` → KEY_HOME). — [README shrinathpol](https://raw.githubusercontent.com/shrinathpol/mstar-bin-tool/HEAD/README.md)

### Inferences
- En un binario NOS sin IR Bin, para cambiar de remoto hay que parchear:
  - (a) las constantes inmediatas del código de cliente en la inicialización del IR (escritura a `IR_CCODE_L/H`) y en las comparaciones de software;
  - (b) los códigos de tecla, que pueden estar como inmediatos en comparaciones, como tabla de traducción o como jump table del `switch`.
- La herramienta necesita patrones de búsqueda de instrucciones AEON y no solo búsqueda de tablas de datos.
- Si el dump contiene el recurso IR Bin (o una cabecera 0x77554321 `BIN_IRMAP_HEADER_ID`), remapear es mucho más seguro: se editan los datos, se recalcula la suma de 32 bits y el CRC16 del recurso, y no se toca la app.
- Cabe la posibilidad de que los "keycode tables" observados dentro de la app del proyecto sean el `g_u16IRkeyMap` por defecto o una tabla `IR_*.h` compilada como array. Hay que verificarlo en el dump.

### Gaps
- No se encontraron los valores numéricos de `IRBIN_KEYMAP_CODE`/`IRBIN_BIN_END` ni la definición de `IRBIN_NUM` (están en otra cabecera no consultada).
- No se halló documentación de foros sobre parcheo IR en V59/V56.

---

## 5. Modos de fallo y riesgos de brickeo reportados

### Takeaway
La evidencia pública directa es escasa: los foros principales estaban bloqueados o solo aparecían como snippets. Los riesgos que se deducen del formato y de las fuentes son: CRC32 del chunk no actualizado (el bootloader no arranca la app), desajuste de tamaño o solapamiento con recursos de la TEL, panel o resolución incorrectos (sin imagen o imagen corrupta, aunque recuperable), escritura parcial o de mala calidad por ISP VGA, y pérdida de los datos de fábrica y HDCP al flashear un dump ajeno.

### Cited Findings
- "Matching the software dump strictly to the display panel prevents distorted images, inverted colors, or bricked microprocessors". Viene de una página de baja calidad, pero es un consejo coherente. — [edx.ipeindia.org (SEO, baja fiabilidad)](https://edx.ipeindia.org/classic-lantern/peaceful-delta-bluff/hk-t-rt2861v09-firmware)
- Los hilos de Elektroda sobre TP.VST59S giran en torno a encontrar un dump compatible con el panel (JHD315GH-E52/E53), a imagen invertida y a peticiones de firmware flip/mirror. Esto confirma que el mirror y el panel son los cambios más demandados. — [topic3859739](https://www.elektroda.com/rtvforum/topic3859739.html), [topic3617489](https://www.elektroda.com/rtvforum/topic3617489.html), [topic4174233](https://www.elektroda.com/rtvforum/topic4174233.html)
- RT809H: hay placas que no se pueden leer ni escribir por VGA/HDMI, y cables VGA→HDMI que no funcionan con ciertas placas. Se recomienda leer y guardar el firmware antes de nada. — [jestineyong.com](https://jestineyong.com/easy-tv-and-monitor-boards-firmware-programming-with-the-rt809h-programmer/)
- flashrom `mstarddc_spi`: hace falta standby y una VGA inactiva. El driver fue deshabilitado por "open questions and code quality issues". El ISP por DDC es delicado. — [flashrom ML 2015](https://www.flashrom.org/pipermail/flashrom/2015-February/013359.html)
- `DoMsDecompression` rechaza la imagen si la longitud descomprimida no coincide con el u32 final. Cualquier desajuste en el trailer de longitud da un fallo de carga. — [ms_decompress.c](https://raw.githubusercontent.com/shinyquagsire23/mstar_extract_decompress/master/ms_decompress.c)
- El bootloader verifica la entrada final 0xF000 de la TEL ("It will be verified by bootloader for integrity check"). — [InfoBlock.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/project/image/dvbt/InfoBlock.c)
- `MApp_IR.c` imprime "IR Bin checksum is wrong!!" si la suma no cuadra, pero sigue adelante (solo es un aviso). — [MApp_IR.c](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/dvb/app/MApp_IR.c)
- La flash termina con bancos de HDCP_KEY, FACTORY_SETTING, CH_DB y GEN_SETTING. Al flashear un dump de otra placa se sobrescriben las claves HDCP y los ajustes de fábrica. — [MApp_FlashMap.h](https://raw.githubusercontent.com/xuzemin/mstar/master/Chakra3_017a0d6_20170731/tv-ap/common/app/include/MApp_FlashMap.h)

### Inferences
Lista de comprobaciones que debería implementar la herramienta:
1. Recalcular el CRC32 (0x04C11DB7, init 0, sin reflejar, sin xorout) sobre el rango exacto y verificarlo contra el original antes de parchear (prueba de "round-trip").
2. Comprobar que el AP recomprimido cabe antes del primer recurso de la TEL (`min(B_FAddr)`), respetar las alineaciones y actualizar "fin en flash" y `file_size`.
3. Mantener o regenerar el trailer de longitud LE si existe.
4. Preservar sin tocar los bancos de DB del final, para no perder HDCP ni los ajustes de fábrica.
5. Validar la cadena de CRC16 por recurso tras editar el logo o el IR.
6. Detectar y recalcular el CRC16 y el CRC32 globales al final de la imagen si existen.
7. Avisar de que un panel o mirror erróneo normalmente se recupera reflasheando por SPI, porque el SBOOT no cambia, mientras que dañar el SBOOT (0x0–0xFFFF) o el PM (0x10000–0x1FFFF) exige un programador externo. Nunca escribir en esas zonas.
8. Prever un fallback LZMA (`CompressMagicNum` 0xDEADEF frente a 0xDEADBE) para variantes `COMPRESS7`.

### Gaps
- No se obtuvieron testimonios concretos de técnicos (4pda, kazus.ru, badcaps, foros indios) sobre bricks por CRC o recompresión en V59/V56. Las búsquedas no devolvieron hilos legibles y elektroda.com no era accesible desde la sesión.
- No consta si el SBOOT de estas placas tiene un modo de recuperación por USB cuando falla el CRC del AP. En el SDK hay `BIN_ID_ISP_UPDATE_FLAG 0xE000` y un bootloader con UI (`ENABLE_BL_UI`), lo que sugiere actualización por USB desde el bootloader, pero no está verificado para V59.

# ISA AEON R2 / BA2 (MStar) y aspectos legales de modificar firmware de TV propio

> Fecha de investigación: 2026-11-15. NO es asesoría legal. Restricción de red en esta sesión: no se pudieron abrir directamente ecfr.gov, law.cornell.edu, copyright.gov, wipo.int ni dpej.rae.es (DNS bloqueado). Los datos legales de EE. UU., Cuba y UE provienen de fragmentos de buscador y de conocimiento previo; se marcan como **[no verificado en texto oficial]** donde aplica. GitHub sí fue accesible (vía WebFetch).

## (A1) ¿Está documentada públicamente la ISA BA2 / AEON R2? ¿Qué herramientas públicas existen?

### Takeaway
No existe un manual público oficial de la ISA BA2/AEON (solo folletos comerciales de Beyond Semiconductor/CAST). Sí hay, a nov-2026, herramientas comunitarias en GitHub: un módulo Ghidra para AEON R2 generado a partir de las tablas del ensamblador del vendedor (`eb4x/ghidra-plugin-aeon`), un extractor de tablas ISA (`smx-smx/aeon-isa`) y un módulo Ghidra para AEON1 (`CUB3D/Ghidra-Aeon`). No se encontró soporte en binutils/GCC upstream, Capstone, Rizin/radare2 ni IDA.

### Cited Findings
- Documentación oficial: solo product briefs. La ISA BA2 se describe como "linear 32-bit address space, variable length instructions: 16, 24, 32, or 48 bits, simple memory addressing modes" — [CAST BA21 brief](https://www.cast-inc.com/pdfs/cast_ba21-brief.pdf); [CAST BA2x family table](https://www.cast-inc.com/pdfs/cast_ba2x-family-table.pdf); [CAST BA21](https://cast-inc.com/processors/32-bit/ba21).
- Beyond Semiconductor publicita la familia BA2x (BA20/BA21/BA22/BA25), con >40% de espacio de opcodes libre para extensiones, simulador "basim" y Newlib; no publica manual de ISA — [Beyond Semi processors](https://www.beyondsemi.com/processors); [BA21](https://beyondsemi.com/beyond-ba21-low-power-embedded-processor); [BA20](https://beyondsemi.com/107/ba20-pipelinezero-embedded-processor); [About](https://www.beyondsemi.com/about-us); [CNX 2014 BA20](https://www.cnx-software.com/2014/10/24/beyond-semi-introduces-32-bit-ba20-core-with-cortex-m4-performance-efficiency-and-cortex-m0-silicon-area/).
- Existe un datasheet BA22 filtrado en Scribd ("Beyond Ba22 De Datasheet") — [Scribd (vía espejo)](https://round-lake.dustinice.workers.dev:443/https/www.scribd.com/document/716612852/Beyond-Ba22-De-Datasheet). No se revisó su contenido; estatus de copyright dudoso.
- BA2 ("Beyond Architecture 2") también se usa en los ZigBee NXP/Jennic (JN516x); Aleph Security lo analizó — [Aleph Security, Xiaomi Zigbee pt.2](https://alephsecurity.com/2019/07/09/xiaomi-zigbee-2).
- **`eb4x/ghidra-plugin-aeon`** (Apache-2.0, actualizado 2026-11-01, 24 commits): "Ghidra processor module for the MStar AEON R2 (aeonR2) core"; idiomas `AEON:LE:32:R2`, `AEON:LE:32:R2-harvard`, `AEON:BE:32:R2`; "The spec is generated from the vendor's own tables and measured against the vendor disassembler". Pipeline: `tools/dump_isa.py` → `isa/aeon_isa.json`; `calibrate.py` (impresión de operandos vía objdump del vendedor); `verify_letters.py`; `dash_probe.py` (bits don't-care); `gen_sleigh.py` → `.sinc` — [GitHub eb4x/ghidra-plugin-aeon](https://github.com/eb4x/ghidra-plugin-aeon); [README raw](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
  - Tablas ISA extraídas "straight out of `aeon-elf-as`'s symbol table"; herramientas del vendedor referenciadas: `aeon-elf-as`, `aeon-elf-objdump`, `aeon-elf-sim` — [README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
  - Longitudes: "instructions of 2, 3 or 4 bytes, always big-endian"; longitud por los 3 bits altos del byte 0: `0xx`=3, `100`=2, `101`=4 (bloque DSP/MAC/SIMD), `11x`=4; alineación 1 byte ("instructions start at any byte"). **No menciona instrucciones de 6 bytes (48 bits)** — [README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
  - Datos: big (`-EB`) o little (`-EL -EBinst`); los 3 lenguajes decodifican igual y difieren en loads/stores y orden de pares de registros — [README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
  - Mnemónicos con prefijos `b.`/`bg.` (p. ej. `b.jal`, `b.jr r9`, `bg.loop`, `bg.btb`, `bg.dma_op`) — [README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
  - Limitaciones declaradas: MAC/DSP/SIMD/float y familia cache/`entri`/`reti` son pseudo-ops sin p-code real; tablas de saltos en RAM no resueltas (0 de 31 en el módulo HDCP); "Create Address Tables" desactivado por defecto; en variante Harvard las direcciones de datos se escriben `data:0x…` — [GitHub](https://github.com/eb4x/ghidra-plugin-aeon).
- **`smx-smx/aeon-isa`**: "Mstar AEON Instruction Set Architecture"; "ISA definition extractor for AEON", se compila como `.so` inyectado por `LD_PRELOAD` en `aeon-elf-as`; enlaza un toolchain `r2-elf-linux-1.3.5.14.tar.xz` alojado en `utils/` de CUB3D/Ghidra-Aeon (aquí "r2" parece significar AEON R2, no radare2 — inferencia) — [GitHub smx-smx/aeon-isa](https://github.com/smx-smx/aeon-isa). eb4x se describe como "the static equivalent of the smx-smx/aeon-isa LD_PRELOAD shim" — [README eb4x](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
- **`CUB3D/Ghidra-Aeon`** (Apache-2.0, 5 commits): "currently only `aeon:aeon1` is supported"; aeon1 = "little-endian 32-bit fixed width OpenRisc 1000 fork" con prefijo `b.` en lugar de `l.`; aeon2/aeonR2 = codificación variable (el README dice "8 to 24 bits", lo que **contradice** los 16–32 bits de eb4x y los 16–48 de CAST; probablemente impreciso) — [GitHub CUB3D/Ghidra-Aeon](https://github.com/CUB3D/ghidra-aeon).
- Búsquedas en GitHub de "aeon ghidra", "aeon sleigh", "ba2 sleigh", "beyond ba2", "aeon disassembler", "jennic ghidra", "ba22 ghidra", "aeon-elf" devolvieron 0 repos; solo "mstar aeon"/"aeon r2" encontraron los dos repos anteriores (consulta `gh search repos`, 2026-11-15). Búsqueda web de nombres sugeridos ("ida-ba2", "binutils-ba2", "beyond-ba2 SLEIGH") sin resultados — [búsqueda web](https://cast-inc.com/processors/32-bit/ba21) (sin hits relevantes).

### Inferences
- Los prefijos `bt.`/`bn.`/`bg.` del prototipo encajan con la regla de longitud de eb4x: `bt.` ≈ 16 bits (patrón `100`), `bn.` ≈ 24 bits (`0xx`), `bg.` ≈ 32 bits (`101`/`11x`). (Inferencia; recuerdo —no verificado— que el BA2 genérico de Beyond/Jennic añade `bw.` de 48 bits.) **Para AEON R2 de MStar, según eb4x, no hay 48 bits**: si el prototipo implementa 48 bits, verificar que no se trata de la ISA BA2 genérica (Jennic/CAST) mezclada con la de AEON R2.
- Las "tablas oficiales" del prototipo probablemente proceden del mismo origen que eb4x/smx-smx (tabla de símbolos/opcodes de `aeon-elf-as` del toolchain MStar). Ese toolchain es software propietario filtrado: usarlo localmente para validar es práctica común, pero redistribuirlo (o tablas copiadas literalmente) tiene riesgo de copyright. Generar las tablas en tiempo de build desde un toolchain que el usuario aporte es más limpio.
- No hay soporte AEON/BA2 conocido en Capstone, Rizin/radare2, IDA o binutils upstream: no se encontró ninguna fuente; Ghidra (eb4x) es hoy la referencia independiente más útil.

### Gaps
- No se pudo descargar `isa/aeon_isa.json` ni `.sinc` de eb4x (API GitHub no habilitada; solo README vía WebFetch) para contrastar opcodes concretos (`bg.movhi`, `bn.sfeqi`, `bn.bnf`).
- No se confirmó la existencia de un GCC/binutils "aeon-elf" público con código fuente (GPL obligaría a publicarlo, pero no se localizó).
- Correspondencia exacta bt/bn/bg/bw ↔ 16/24/32/48 en documentación primaria: no encontrada.

## (A2) Convención de llamada, registros callee-saved y delay slots

### Takeaway
Según eb4x (derivado del GCC del vendedor): r0=0, r1=SP, r9=link, args r3–r8, retorno r3 (r3:r4 para 64 bits), callee-saved r10–r22, sin delay slots en aeonR2.

### Cited Findings
- "Stack pointer: r1"; "Return address: r9 — `b.jal` writes it, `b.jr r9` returns"; "Arguments: r3..r8, then the stack at 0(r1)"; "Return value: r3; 64-bit r3+r4 (orden según endianness)"; "Callee-saved: r10..r22; r23..r31 usados libremente por funciones hoja"; "r0 hardwired to zero" — [eb4x README](https://github.com/eb4x/ghidra-plugin-aeon).
- Delay slots: ninguno; el driver gcc del vendedor "rejects `-minsert-nop-before-branch` for `-march=aeonR2`", y la salida aeonR2 no tiene slots rellenos — [eb4x](https://github.com/eb4x/ghidra-plugin-aeon).
- AEON1 (32 bits fijo, fork de OR1K): retorno en r3; parámetros 64 bits en par `r12:r11` (invertido respecto a OpenRISC) — [CUB3D/Ghidra-Aeon](https://github.com/CUB3D/ghidra-aeon).

### Inferences
- Difiere de OpenRISC 1000 estándar (OR1K: callee-saved r10,r14,r16…r30 pares; con delay slot). No asumir semántica OR1K al parchear (p. ej. no insertar NOP tras saltos en R2).
- Flag de comparación: `bn.sfeqi` + `bn.bnf`/`bf` sigue el modelo OR1K (set-flag + branch-on-flag) — inferencia por nomenclatura, no verificado.

### Gaps
- No verificado el uso de r2 (frame pointer?), r10 (TLS en OR1K) ni r11 en R2.

## (A3) Portar el ensamblador/desensamblador Python a C# y verificarlo

### Takeaway
Mantener la ISA como datos (JSON/tabla generada) compartidos por ambas implementaciones, y validar con pruebas diferenciales: Python vs C#, round-trip asm↔bytes, y comparación contra Ghidra (eb4x) y, si se dispone, contra `aeon-elf-objdump`.

### Cited Findings
- Metodología de referencia de eb4x: diff dirección-a-dirección (mnemónico, operandos, longitud) contra el objdump del vendedor en barrido lineal de 5 fixtures (sBoot, módulo HDCP, dos builds stream 0, slice TSUM_G) con "zero unexplained differences"; bytes `.word` contabilizados aparte; 35 casos de emulación (`gen_emu_cases.py` → p-code emulator, `AeonEmuTest.java`); "The acceptance diff proves decoding, not code coverage" — [eb4x README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
- Script `ghidra_scripts/AeonDumpDisasm.java` hace volcado lineal — [eb4x](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).

### Inferences (recomendaciones)
- **Fuente única de verdad**: exportar las tablas del prototipo Python a JSON (máscara, valor, longitud, operandos con campos de bits/signo/escala) y que C# las cargue (o generarlas como código con un source generator). Evita divergencias de transcripción.
- **Pruebas en capas** (xUnit/NUnit):
  1. *Golden vectors*: para cada mnemónico, bytes→texto generados por Python; C# debe coincidir byte a byte y texto a texto.
  2. *Round-trip exhaustivo*: para cada entrada de tabla, enumerar/aleatorizar operandos (incl. bordes: inmediatos mín/máx, negativos, desplazamientos de rama ±) → assemble → disassemble → assemble == bytes originales. Para 16/24 bits, se puede barrer todo el espacio (2^16, 2^24) en disassemble→assemble.
  3. *Fuzzing diferencial*: bytes aleatorios → disasm Python vs C# (FsCheck o bucle simple).
  4. *Contra Ghidra*: correr `AeonDumpDisasm.java` (o un script headless `analyzeHeadless … -postScript`) sobre el mismo binario y diffear mnemónico/longitud.
  5. *Barrido lineal real*: el desensamblado de una imagen de firmware completa debe resincronizar igual que Ghidra/objdump (longitud por 3 bits altos, big-endian).
- Detalles de C#: usar `ReadOnlySpan<byte>`/`BinaryPrimitives.ReadUInt32BigEndian` (instrucciones siempre big-endian aun con datos LE); cuidado con extensión de signo (`(int)(x << (32-n)) >> (32-n)`), desplazamientos relativos a PC (verificar si relativos a la instrucción actual o siguiente con Ghidra), y aliases/pseudo-ops que el disasm imprime distinto de lo que el asm acepta.
- Al parchear: el código es de longitud variable y alineación 1, así que un reemplazo debe ocupar exactamente los mismos bytes o rellenar con NOP de la longitud adecuada; verificar que el parche no cae en mitad de otra instrucción (barrido desde un límite conocido).

### Gaps
- No se encontró una suite pública de vectores de prueba AEON independiente.

## (A4) ¿Puede usarse un script/módulo Ghidra para validar parches?

### Takeaway
Sí, con matices: el módulo eb4x permite desensamblar/decompilar y emular p-code, por lo que un script headless puede aplicar el parche a una copia y comprobar decodificación, límites y flujo; la semántica DSP/MAC/SIMD no está modelada.

### Cited Findings
- eb4x ofrece SLEIGH + emulación p-code verificada contra `aeon-elf-sim` (35 casos) y decompilación sin fallos en HDCP/stream 0; pseudo-ops sin p-code para MAC/DSP/SIMD/float/cache — [eb4x](https://github.com/eb4x/ghidra-plugin-aeon).
- Ghidra carga módulos de procesador SLEIGH como extensiones (p. ej. instalar en Ghidra/Processors o "File > Install Extensions") — [NCC Group nanoMIPS](https://www.nccgroup.com/research/ghidra-nanomips-isa-module/); [ghidra-mn102-lang](https://awesome.ecosyste.ms/projects/github.com%2Fpokechu22%2Fghidra-mn102-lang). eb4x: `./gradlew buildExtension` / `installExtension` — [README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).

### Inferences
- Flujo propuesto: (1) aplicar perfil de parche a una copia; (2) `analyzeHeadless` con `AEON:LE:32:R2` (o BE según el SoC); (3) script Java/Python que, para cada rango parcheado, verifique: decodifica sin `??`, longitud total = bytes reemplazados, destinos de salto dentro de funciones válidas, no se pisan referencias/tablas; (4) opcional: emular la función parcheada con `EmulatorHelper` y comparar registros esperados. El ensamblador SLEIGH de Ghidra (Patch Instruction) también podría servir como segundo ensamblador de referencia, aunque su calidad para ISAs de longitud variable no está verificada para AEON.

### Gaps
- No verificado si el "Patch Instruction"/assembler de Ghidra funciona bien con el `.sinc` de eb4x.

## (B1) Legalidad de modificar el firmware de un TV propio (reparación/compatibilidad)

### Takeaway
En EE. UU. existe exención DMCA 1201 (2024) para reparar dispositivos de consumo y otra específica para "jailbreak" de smart TVs; en la UE la Directiva de software permite al usuario legítimo corregir errores y descompilar para interoperabilidad, y la Directiva de reparación 2024/1799 prohíbe trabas de software a la reparación de productos del Anexo II (incluye "pantallas electrónicas"). En Cuba rige la Ley 154/2022 que protege los programas, pero no se pudo verificar qué excepciones contempla. Riesgo práctico bajo para uso personal; mayor al distribuir.

### Cited Findings
- **EE. UU.**: la 9.ª rulemaking trienal (efectiva 28-oct-2024) renovó la exención para programas que controlan dispositivos "primarily designed for use by consumers" para diagnóstico, mantenimiento y reparación — [Finnegan](https://www.finnegan.com/en/insights/ip-updates/final-rule-issued-in-the-us-copyright-offices-ninth-triennial-section-1201-proceeding.html); [Mondaq](https://www.mondaq.com/unitedstates/copyright/1538214/final-rule-issued-in-the-us-copyright-offices-ninth-triennial-section-1201-proceeding); [iFixit, petición renovación 2027](https://www.copyright.gov/1201/2027/petitions/renewal/Renewal-Pet-Device-Repair-iFixit.pdf).
- Existe una exención separada para programas en smart TVs para permitir interoperar con aplicaciones (no para acceso no autorizado a obras); EFF pidió su renovación en el ciclo 2027 — [EFF renewal, Smart TVs](https://www.copyright.gov/1201/2027/petitions/renewal/Renewal-Pet-Jailbreaking-Smart-TVs-Electronic-Frontier-Foundation.pdf); [SFC 2024](https://sfconservancy.org/news/2024/oct/31/success-in-2024-dmca-exemptions/).
- Las exenciones duran 3 años; el 10.º ciclo (2027) está en curso a nov-2026 — [EFF renewal pet.](https://www.copyright.gov/1201/2027/petitions/renewal/Renewal-Pet-Jailbreaking-Smart-TVs-Electronic-Frontier-Foundation.pdf).
- Definiciones de "maintenance"/"repair" de la exención excluyen ciertas actividades (debate AdvaMed sobre exclusiones) — [AdvaMed opp.](https://www.copyright.gov/1201/2027/petitions/renewal/Opp-Medical-Devices-Advanced-Medical-Technology-Association-AdvaMed-1.pdf). Texto oficial: [37 CFR 201.40](https://www.copyright.gov/title37/201/37cfr201-40.html) **[no verificado: host bloqueado]**.
- Defensores argumentan que la reparación es no infractora bajo §107 (fair use) y §117 — [iFixit/Repair.org 2021](https://www.copyright.gov/1201/2021/comments/Class%2012_InitialComments_iFixit%20and%20Repair%20Association.pdf) (posición de parte, no resolución).
- **UE – Directiva 2009/24/CE**: art. 6 permite reproducir/traducir código sin autorización para lograr interoperabilidad de un programa creado independientemente (con condiciones); art. 7/recital: cláusulas contractuales contrarias al art. 6 o al art. 5(2)-(3) son nulas — [legislation.gov.uk art. 6](https://www.legislation.gov.uk/eudr/2009/24/article/6); [EUR-Lex 2009/24](https://eur-lex.europa.eu/eli/dir/2009/24/oj). Art. 5(1) (actos necesarios para el uso por el adquirente legítimo, "incluida la corrección de errores") y 5(3) (observar/estudiar/probar) — de conocimiento previo, **no confirmado en el fragmento obtenido**. En España se transpone en arts. 99–100 TRLPI (RDL 1/1996) **[conocimiento previo, no verificado aquí]**.
- **UE – Directiva (UE) 2024/1799 (derecho a reparar)**: art. 5 prohíbe a fabricantes usar cláusulas contractuales, técnicas de hardware o software que impidan la reparación de bienes del Anexo II salvo factores legítimos y objetivos (incl. protección de PI); Anexo II incluye "electronic displays"; transposición hasta 31-jul-2026 — [Bird & Bird](https://cm.twobirds.com/en/insights/2024/germany/recht-auf-reparatur-richtlinie-veroeffentlicht-auswirkungen-fuer-wirtschaftsakteure-in-der-eu); [Comisión Europea](https://commission.europa.eu/law/law-topic/consumer-protection-law/directive-repair-goods_en); [art. 5](https://gesetze.legal/eu/rl_2024_1799_eu/5); [Lewis Silkin 2026](https://www.lewissilkin.com/insights/2026/03/18/what-does-the-right-to-repair-directive-mean-for-your-business).
- **Cuba**: Ley 154/2022 "De los Derechos del Autor y del Artista Intérprete", Gaceta Oficial No. 122 Ordinaria de 5-dic-2022 (GOC-2022-1103-O122), sustituye a la Ley 14/1977; incluye protección de programas de computación — [WIPO Lex](https://www.wipo.int/wipolex/es/legislation/details/21621); [DPEJ](https://dpej.rae.es/eli/cu/l/2022/05/06/154); [Univ. de Oriente](https://santiago.uo.edu.cu/index.php/stgo/article/download/28778/5272/522180). Fechas discrepantes entre fuentes (6-may vs 21-nov-2022).
- Norma cubana anterior sobre programas (Resolución Conjunta No. 1): reproducción requiere autorización "incluso personal", salvo copia de seguridad e introducción en memoria interna; transformación sujeta a autorización — [WIPO cu033es](https://wipolex-resources-eu-central-1-358922420655.s3.amazonaws.com/edocs/lexdocs/laws/es/cu/cu033es.pdf) (vigencia tras Ley 154 no confirmada).

### Inferences
- EE. UU.: §1201 solo importa si se elude una medida tecnológica (firma, cifrado, bloqueo). Parchear un firmware MStar sin cifrar/firmar puede no implicar 1201; la modificación del propio ejemplar podría ampararse en 17 U.S.C. §117(a) ("essential step"/adaptación por el propietario) — **[no verificado aquí: 17 U.S.C. 117]**. Un TV es, con alta probabilidad, "dispositivo diseñado principalmente para consumidores", y cambiar panel o mando es plausiblemente "repair/maintenance"; pero la exención **no** cubre el tráfico de herramientas de elusión (§1201(a)(2)/(b)) — distribuir una herramienta que rompa firmas sería lo más arriesgado **[conocimiento previo]**.
- UE/España: adaptar el firmware para que funcione con un panel de reemplazo encaja razonablemente en "corrección de errores/uso conforme a su finalidad" (art. 5.1) más que en el art. 6 (que es para interoperabilidad de *otro programa*); incierto. La Directiva 2024/1799 obliga a fabricantes, no da por sí sola un derecho de modificar software al consumidor.
- Cuba: sin acceso al texto de excepciones de la Ley 154; la aplicación práctica a particulares que reparan su propio TV es probablemente nula, pero no hay base escrita confirmada. Cuba no tiene (según lo encontrado) normas anti-elusión equivalentes a DMCA — **no verificado**.

### Gaps
- Texto literal de 37 CFR 201.40(b) (número de párrafo vigente) y de 17 U.S.C. §117/§1201 no verificado.
- Artículos concretos de la Ley 154/2022 sobre programas, límites y medidas tecnológicas: no obtenidos.
- Confirmación de que "electronic displays" del Anexo II cubre televisores (Reglamento de ecodiseño (UE) 2019/2021): no confirmado en fuentes obtenidas.
- Estado de transposición en España a nov-2026: no verificado.

## (B2) Distribuir la herramienta vs. distribuir firmware modificado; avisos y seguridad

### Takeaway
Distribuir una herramienta propia y "perfiles de parche" (offset, bytes esperados, bytes nuevos, hash del original) es mucho menos arriesgado que redistribuir binarios del fabricante (modificados o no), que son obra protegida. Incluir descargo de garantía tipo MIT/GPL, aviso de riesgo de bricking y advertencias eléctricas.

### Cited Findings
- Directiva 2009/24, art. 6(2): la información obtenida por descompilación no puede usarse para otros fines ni comunicarse a terceros salvo necesidad para la interoperabilidad — [legislation.gov.uk art. 6](https://www.legislation.gov.uk/eudr/2009/24/article/6) (contenido del apartado según conocimiento previo; el fragmento obtenido solo confirma tema y condiciones).
- eb4x ilustra el patrón "no redistribuir el toolchain del vendedor": la reensamblación con el toolchain del vendedor es "local only; CI checks the committed bytes" — [eb4x README](https://raw.githubusercontent.com/eb4x/ghidra-plugin-aeon/main/README.md).
- Licencias Apache-2.0 usadas por los módulos Ghidra comunitarios (incluyen cláusula "AS IS"/sin garantía) — [eb4x](https://github.com/eb4x/ghidra-plugin-aeon); [CUB3D](https://github.com/CUB3D/ghidra-aeon).

### Inferences (recomendaciones)
- Formato de perfil: `{modelo/board, versión fw, sha256 del original, [ {offset, bytes_esperados, bytes_nuevos, descripción} ]}`; la herramienta aplica solo si el hash/bytes coinciden y genera backup. No incluir fragmentos grandes de código del fabricante en los perfiles (unos pocos bytes de contexto son de minimis, probablemente).
- No incluir el toolchain `aeon-elf`/`r2-elf` filtrado ni tablas copiadas literalmente; ofrecer script que las genere si el usuario aporta su toolchain.
- Descargo típico: "SE PROPORCIONA TAL CUAL, SIN GARANTÍA…"; "puede inutilizar el equipo (brick) y anular la garantía"; "haga copia completa de la SPI flash con programador (CH341A, etc.) antes de escribir"; "uso exclusivo en equipos de su propiedad; respete la legislación local"; "no es asesoría legal".
- Seguridad eléctrica (conocimiento general, sin fuente verificada en esta sesión): inversores CCFL generan alta tensión (cientos a >1 kV AC) y la fuente primaria tiene condensadores a ~300–400 V DC; desconectar y descargar antes de manipular. Seleccionar mal el VCC del panel (3,3/5/12 V) en firmware o jumpers puede destruir la T-CON/panel: verificar datasheet del panel y medir antes de conectar LVDS/V-by-One; ajustes de retroiluminación (corriente LED) fuera de rango acortan la vida o queman LEDs.

### Gaps
- No se recogieron con fuente ejemplos de descargos de herramientas concretas de reflasheo MStar (p. ej. foros de técnicos) ni cifras de tensión de inversores CCFL con referencia.

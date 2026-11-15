# Herramientas AEON R2 (ensamblador / desensamblador)

| Archivo | Qué es |
|---|---|
| `aeondis.py` | Desensamblador mínimo AEON R2 (instrucciones big-endian de 2, 3 o 4 bytes). Decodifica con la tabla `aeon_isa.json`. |
| `aeonasm.py` | Ensamblador inverso: `asm(nombre, {letra: valor}, pc) -> bytes`. Cada instrucción que genera la vuelve a desensamblar y la compara; si no coincide, da error. |
| `aeon_isa.json` | Tablas de la ISA (opcodes, codificación, letras de operandos con signo, escala, sesgo y relativo al PC). |
| `build_cave_v2.py` | Ejemplo real de uso: genera la rutina de la v5 del LG (`cave_v2.json`). |

## Origen de las tablas

- `aeon_isa.json` es **idéntico** (comprobado con `cmp`) al archivo `isa/aeon_isa.json` del proyecto
  **ghidra-plugin-aeon** (https://github.com/eb4x/ghidra-plugin-aeon), commit `788cf8a6308c1a01c8e47e26fc85598bc88bbd63`.
  Licencia Apache 2.0: si se redistribuye, hay que incluir la licencia y el aviso de origen.
- Según el README de ese proyecto, el JSON se extrae con `tools/dump_isa.py` de la tabla de
  símbolos del ensamblador del fabricante (`aeon-elf-as`). Es decir, sale de la herramienta del fabricante,
  no de codificaciones deducidas a mano. Esa es la razón por la que lo llamé "tablas oficiales".
- La semántica de cada instrucción (por ejemplo `cmovri rD,rA,I` = flag ? rA : I) está en
  `data/languages/aeonR2.sinc` del mismo proyecto. Para consultarla, mejor clonar el repositorio.
- También consulté https://github.com/throwaway96/ghidra-aeon (commit `79efa6fb…`, Apache 2.0).
  No se usa en los scripts.

## Uso

Los tres archivos tienen que estar en la misma carpeta (`aeondis.py` busca `aeon_isa.json` junto a él).

```python
from aeonasm import asm
from aeondis import Dis, fmt
b = asm('bg.jal', {'/': 0x3BB400}, 0x2B4A60)      # -> e420d340
r = Dis().decode(b, 0, 0x2B4A60)                   # (longitud, nombre, args, operandos)
print(fmt(r[1], r[2], r[3]))                       # bg.jal 0x3bb400
python3 aeonasm.py bn.lhz bg.ori                   # muestra las letras de operando de cada instrucción
```

## Limitaciones

- Es un desensamblador lineal: no sigue el flujo del código y no separa código de datos.
- Ante varias codificaciones posibles, elige la primera entrada de la tabla que coincide.
- Ya se usaron con los firmwares Konka y LG, y se verificaron contra el código real de esos binarios.
  No es un ensamblador completo: no tiene etiquetas ni directivas. Las etiquetas se resuelven en
  el script que lo llama (ver `build_cave_v2.py`).

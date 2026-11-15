# Avisos de terceros

FirmReaper incluye código o datos de los siguientes proyectos. Todos con licencia **Apache 2.0**;
el texto completo está en la carpeta [`licenses/`](licenses/).

## Zopfli (Google)

- Origen: biblioteca C de Zopfli incluida en **py-zopfli 0.4.3** (PyPI `zopfli==0.4.3`,
  carpeta `zopfli/src/zopfli`; fork fonttools/zopfli).
- Copyright 2011 Google Inc. Autores: Lode Vandevenne, Jyrki Alakuijala.
- Uso en FirmReaper: **port a C#** en `src/FirmReaper.Core/Compression/Zopfli/`
  (`Symbols.cs`, `ZopfliCore.cs`, `ZopfliTrees.cs`, `ZopfliSqueeze.cs`, `ZopfliDeflate.cs`).
- Cambios respecto al original: traducción a C#; punteros sustituidos por índices; sin salida
  `verbose`; solo el modo de bloque automático (btype 2) que usan los scripts; se quitaron los `assert`.
  El resultado es idéntico byte a byte (lo comprueban las pruebas).
- Licencia: `licenses/Apache-2.0_zopfli.txt`.

## ghidra-plugin-aeon (eb4x)

- Origen: https://github.com/eb4x/ghidra-plugin-aeon, commit `788cf8a6308c1a01c8e47e26fc85598bc88bbd63`,
  archivo `isa/aeon_isa.json` (tablas de la ISA AEON R2, extraídas del ensamblador `aeon-elf-as`).
- Uso en FirmReaper: `tools/aeon/aeon_isa.json` (copia idéntica, comprobada por SHA-256
  `633939287d1f2bda24398f36676004a824725d1c307b12e31cc8193bd74a6948`).
- Licencia: `licenses/Apache-2.0_ghidra-plugin-aeon.txt`.

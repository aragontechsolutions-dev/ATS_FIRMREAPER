# Avisos de terceros

FirmReaper incluye código o datos de los siguientes proyectos (licencias Apache 2.0 y BSD de 3 cláusulas);
el texto completo de cada licencia está en la carpeta [`licenses/`](licenses/).

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

## LibJpeg.NET (Bit Miracle) — basado en el software JPEG del Independent JPEG Group

- Paquete NuGet `BitMiracle.LibJpeg.NET` 1.5.324 (código gestionado, sin partes nativas).
- Copyright (c) 2008-2020, Bit Miracle. Basado en parte en el software del Independent JPEG Group
  (http://www.ijg.org/), Copyright (C) 1991-1998, Thomas G. Lane.
- Uso en FirmReaper: **decodificar** el logo original antes de girarlo (`Imaging/LogoImage.cs`).
- Licencia: `licenses/BSD-3-Clause_LibJpeg.NET.txt`.

## Codificador JPEG propio

`Imaging/JpegBaselineEncoder.cs` es código propio que reproduce los algoritmos de libjpeg-turbo
(jccolor.c, jfdctint.c, jcdctmgr.c, jchuff.c, jcmarker.c; licencias IJG/BSD) para que el logo girado salga
idéntico byte a byte al de Pillow. This software is based in part on the work of the Independent JPEG Group.

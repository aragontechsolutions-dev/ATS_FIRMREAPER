# Perfiles JSON, seguridad, verificación y pruebas para un parcheador de firmware MStar (C# .NET 8 WPF)

> Notas de investigación (nov. 2026). Limitación importante: en esta sesión no se pudieron abrir con WebFetch learn.microsoft.com, romhacking.net ni tunerpro.net (DNS bloqueado por la red de la sesión). Los datos citados vienen de resultados de WebSearch (fragmentos). Lo marcado **[NO VERIFICADO]** procede de conocimiento previo y no se confirmó con fuentes en esta sesión.

## 1. Prior art: cómo verifican integridad los formatos de parche y las herramientas de definición (IPS/BPS/UPS/xdelta, TunerPro XDF, RomRaider, BIOS, trainers)

### Takeaway
Los formatos modernos (BPS, UPS, xdelta/VCDIFF) llevan un hash/CRC del **origen** y del **destino** (y del propio parche); IPS no lleva nada y por eso es el contraejemplo clásico. Las herramientas "por definición" (TunerPro XDF, RomRaider) separan **qué es la imagen** (identificación por ID en una dirección) de **cómo interpretar zonas** (tablas, escalados, enums, flags). Nuestro perfil debe combinar ambas ideas: SHA-256 de entrada + SHA-256 de salida esperada + definiciones tipadas por dirección.

### Cited Findings
- BPS: cabecera "BPS1", tamaños de origen/destino, metadatos opcionales, acciones (SourceRead, TargetRead, SourceCopy, TargetCopy) y pie con tres CRC32: origen, destino y parche (el CRC del parche cubre todo salvo los últimos 4 bytes) — [Gist espejo de la especificación de byuu](https://gist.github.com/anonymous/bd56fc12003b3fde03f122d681828040); [vidua docs BPS](https://vidua.readthedocs.io/en/latest/bps.html); el hilo de NESdev indica que la spec oficial está en romhacking.net "BPS File Format Specification" — [NESdev forum](https://forums.nesdev.org/viewtopic.php?p=209682).
- Implementaciones (igir en TypeScript, flips en Rust) validan los CRC32 al aplicar — [igir bpsPatch.ts](https://codeclimate.com/github/emmercm/igir/src/types/patches/bpsPatch.ts/source); [flips.rs](https://github.com/althonos/flips.rs).
- UPS: campos source_crc32, target_crc32 y patch_crc32 — [crate ups (docs.rs)](https://docs.rs/ups/latest/ups/struct.UpsPatch.html); [igir upsPatch.ts](https://codeclimate.com/github/emmercm/igir/src/types/patches/upsPatch.ts/source).
- IPS: no tiene delta-encoding ni verificación de que se aplica al archivo correcto; xdelta sí la tiene — [Data Crystal: Patch](https://datacrystal.romhacking.net/wiki/Patch); [SnesLab Patch Formats](https://sneslab.net/wiki/Patch_Formats).
- xdelta3 guarda los deltas en formato VCDIFF (RFC 3284); un error de checksum al aplicar significa que el archivo de origen no es el esperado; `--noverify` desactiva la verificación MD5 — [man xdelta](https://www.unix.com/man-page/suse/1/xdelta/); [Debian xdelta3](https://packages.debian.org/sid/xdelta3). (La afirmación de que xdelta3 usa Adler-32 para ventanas de origen **[NO VERIFICADO]**.)
- TunerPro XDF: el `<baseoffset>` está en `XDFHEADER`, se escribe en **decimal** y puede ser negativo — [HP Tuners, User Defined Parameters](https://docs.hptuners.com/VCM_Editor_User_Defined_Parameters/01_UserDefinedParameters/). Ejemplo de tabla `<XDFTABLE uniqueid="0xC0CA" flags="0x0">` con ejes — [foro TunerPro](https://www.tunerpro.net/forum3/viewtopic.php?p=8526). Existe página de ayuda de "Flags" — [TunerPro WebHelp Flags](https://www.tunerpro.net/WebHelp/source/Flags.htm).
- TunerPro: el cálculo de checksum integrado sólo cubre métodos simples; no permite excluir áreas, para algoritmos complejos hay que añadir soporte/plugin — [foro TunerPro p=6338](https://www.tunerpro.net/forum3/viewtopic.php?p=6338). Hay plugin de import/export XDF para WinOLS (comercial) — [EVC plugins](https://www.evc.de/en/download/plugins.asp). Las definiciones XDF/ADX/BIN se describen en [HP Academy: What is an XDF file](https://www.hpacademy.com/blog/what-is-an-xdf-file/).
- RomRaider: cada definición es un `<rom>` con `<romid>` que contiene `xmlid`, `internalidaddress`, `internalidstring` (CAL ID), `caseid`, `ecuid`…; `internalidaddress` es la dirección hex donde está la cadena de ID en la imagen; un `<rom base="...">` hereda de otra definición; los `<scalingbase>` compartidos van antes de los `<rom>` y cada `<rom>` contiene `<table>` — [RomRaider Editor XML Reference Guide](https://mailserver.romraider.com/index.php/RomRaider/EditorXMLReferenceGuide); [foro RomRaider p=499](https://mailserver.romraider.com/forum/viewtopic.php?p=499). Un `internalidstring` con espacios ("00 73 0A 66") no casaba; quitar espacios lo arregló — [foro RomRaider](https://mailserver.romraider.com/forum/viewtopic.php?p=143194).
- **[NO VERIFICADO]** Conceptos XDF adicionales: XDFCONSTANT (valor escalar con dirección, tamaño en bits, ecuación MATH con variable X), XDFTABLE (ejes X/Y/Z con count, stride, ecuación), XDFFLAG (bitmask sobre un byte/word = booleano), XDFPATCH (pares base/patch de bytes con comprobación "is applied"), XDFCHECKSUM (rango inicio/fin, ubicación del checksum, plugin DLL). WinOLS "map packs": conjuntos de mapas (dirección, dimensiones, factor/offset) exportables e importables con búsqueda por patrones si la dirección cambia; checksums vía módulos de pago. Cheat Engine (.CT): entradas con dirección/AOB-scan, tipo, y "Auto Assemble" con secciones [ENABLE]/[DISABLE] que verifican bytes originales antes de escribir. UEFITool: parsea el árbol de volúmenes/ficheros UEFI y valida checksums de cabeceras FFS; AMIBCP edita setup a nivel de estructura, no bytes crudos.

### Inferences
- Diseño recomendado del perfil, inspirado en BPS+RomRaider:
  - `target`: `{ "sha256": "...", "size": 4194304, "idChecks": [ { "address": "0x...", "expectedAscii"|"expectedHex": "..." } ] }` (equivalente a `internalidaddress/internalidstring`, útil para mensajes de error amigables: "es la versión X, no la Y").
  - `patches[]`: `{ id, address, original (hex), replacement (hex), description, group }` — **siempre** con bytes originales (como XDFPATCH / Cheat Engine [DISABLE]) para poder: verificar precondición, detectar "ya aplicado" (bytes == replacement), y revertir.
  - `expectedOutputSha256` por combinación de opciones cuando sea finita (BPS target CRC). Si hay tablas editables/logos, no es posible; usar entonces el "diff esperado" (sección 4).
  - `options`: enums/booleanos → conjuntos de `patchId` (como XDFFLAG/enums); `tables`: dirección, filas×columnas, tamaño de elemento, endianness, codificación de cada celda, dominio permitido (como XDFTABLE/scaling); `logoSlots`: dirección, `maxSize`, formato, relleno.
  - `checksums[]` (si el firmware MStar tiene CRC internos de particiones): algoritmo, rango, ubicación; recalcular en código, nunca desde datos arbitrarios del perfil (TunerPro muestra que "plugins" de checksum son fuente de complejidad).
- Usar SHA-256 (no CRC32) porque CRC32 no resiste manipulación intencionada; CRC sólo protege contra errores accidentales.

### Gaps
- No se pudo leer la spec oficial XDF ni la página de ayuda de TunerPro (bloqueado); el detalle de XDFCONSTANT/XDFPATCH/XDFCHECKSUM queda sin verificar.
- No se buscaron fuentes primarias sobre UEFITool/MMTool/AMIBCP ni Cheat Engine .CT (límite de llamadas).

## 2. Validación de esquema JSON en .NET 8 y deserialización estricta

### Takeaway
Usar **System.Text.Json** con deserialización estricta (`JsonUnmappedMemberHandling.Disallow`, `required`, límites de profundidad/tamaño) + **JsonSchema.Net** (json-everything, draft 2020-12) como segunda barrera y para mensajes de error a terceros autores de perfiles. Evitar Newtonsoft.Json.Schema por su licencia AGPL/limitación de 1000 validaciones/hora en la versión gratuita.

### Cited Findings
- `JsonUnmappedMemberHandling` (desde .NET 8) tiene `Skip` (por defecto) y `Disallow` (lanza excepción ante propiedad no mapeada); se aplica por tipo con `[JsonUnmappedMemberHandling]` o globalmente con `JsonSerializerOptions.UnmappedMemberHandling` — [API JsonUnmappedMemberHandling (.NET 8)](https://learn.microsoft.com/en-gb/dotnet/api/system.text.json.serialization.jsonunmappedmemberhandling?view=net-8.0); [Handle missing members during deserialization](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/missing-members); [Start Debugging](https://startdebugging.net/2023/09/net-8-handle-missing-members-during-json-deserialization/).
- JsonSchema.Net soporta draft 6 en adelante, incluido 2020-12 (draft 4 no formalmente) — [json-everything docs](https://docs.json-everything.net/schema/basics); [NuGet JsonSchema.Net 7.3.0](https://feed.nuget.org/packages/JsonSchema.Net/7.3.0).
- Newtonsoft.Json.Schema: nivel AGPL 3.0 gratuito "con limitaciones (1000 validaciones por hora)"; licencias comerciales $129 Indie / $199 Business / $1,299 Site — [newtonsoft.com/jsonschema](https://www.newtonsoft.com/jsonschema); [tienda](https://newtonsoft.com/store/jsonschema). Al superar el cupo lanza excepción de "free-quota limit" — [issue #307](https://github-redirect.dependabot.com/JamesNK/Newtonsoft.Json.Schema/issues/307); registro con `License.RegisterLicense` — [docs License](https://www.newtonsoft.com/jsonschema/help/html/T_Newtonsoft_Json_Schema_License.htm).
- **[NO VERIFICADO]** `required` (C# 11) y `[JsonRequired]` son respetados por System.Text.Json desde .NET 7; `JsonSerializerOptions.MaxDepth` por defecto 64; `JsonSerializerOptions.RespectNullableAnnotations` y `RespectRequiredConstructorParameters` llegaron en .NET 9 (no en .NET 8); `AllowDuplicateProperties=false` (rechazar claves duplicadas) llegó en .NET 10 → en .NET 8 hay que detectar duplicados manualmente con `Utf8JsonReader`/`JsonDocument`. JsonSchema.Net y NJsonSchema son MIT. NJsonSchema está orientado a generación de código/OpenAPI y depende de Newtonsoft.Json; su soporte de 2020-12 es parcial.

### Inferences
- Pipeline de carga recomendado:
  1. Límite de tamaño de archivo (p. ej. 1 MB) antes de leer; leer en `byte[]`.
  2. Verificar integridad (hash/firma, sección 3) sobre los **bytes crudos** antes de parsear.
  3. `JsonDocument.Parse` con `JsonDocumentOptions { MaxDepth = 32, CommentHandling = Disallow, AllowTrailingCommas = false }`; pasada propia para rechazar claves duplicadas (en .NET 8).
  4. Validar contra JSON Schema 2020-12 embebido (`additionalProperties:false`, `pattern` para hex `^0x[0-9A-F]{1,8}$`, `maxItems`, `enum`) con JsonSchema.Net, mostrando `OutputFormat.List` al usuario.
  5. Deserializar a `record`s inmutables con `required`, `UnmappedMemberHandling.Disallow`, `NumberHandling = Strict`, y preferir source generator (`JsonSerializerContext`) — sin polimorfismo por `$type` (OWASP: no deserializar tipos controlados por el atacante).
  6. Validación semántica en código (no expresable en esquema): direcciones < tamaño de imagen, longitud(original)==longitud(new), sin solapes, dentro de zonas, IDs únicos, referencias de opciones a patches existentes, `schemaVersion` soportada.
- Direcciones como **cadenas hex** (no números JSON) para evitar ambigüedad decimal/hex (el error decimal/hex de XDF `baseoffset` es un ejemplo de lo fácil que es equivocarse).

### Gaps
- No se pudo abrir la documentación de Microsoft para confirmar versiones exactas de `RespectRequiredConstructorParameters`, `AllowDuplicateProperties` y `MaxDepth` por defecto.
- No se encontró guía OWASP concreta en esta sesión (no buscada por límite).

## 3. Integridad de perfiles: firma, canonicalización y modelo de amenazas

### Takeaway
Para una app de escritorio con perfiles propios, lo más simple y robusto es: perfiles oficiales **embebidos como recursos** + **lista blanca de SHA-256** compilada; perfiles externos sólo con **firma separada (detached) sobre los bytes exactos del archivo** (evita necesitar JCS/RFC 8785). Ed25519 no está en la BCL de .NET 8; usar **ECDSA P-256 (`ECDsa`)** nativo o BouncyCastle para Ed25519.

### Cited Findings
- Varias fuentes indican que `System.Security.Cryptography` no tiene tipo Ed25519 (ni en .NET 10) y recomiendan BouncyCastle (`Ed25519Signer`); una guía (SSOJet) afirma lo contrario y se considera poco fiable; la propuesta en dotnet/runtime sigue abierta — [dotnet/runtime #14741](https://github.com/dotnet/runtime/issues/14741); [mojoauth Ed25519 C#](https://mojoauth.com/security-guides/generate-keypair-using-ed25519-with-csharp); contradicho por [SSOJet](https://ssojet.com/dev-guides/generate-keypair-using-ed25519-in-csharp).
- .NET 10 añade tipos post-cuánticos (ML-KEM, ML-DSA, SLH-DSA) — [What's new in .NET 10 libraries](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries); [Duende: PQC in .NET 10](https://duendesoftware.com/blog/20260514-post-quantum-cryptography-in-dotnet-10).
- **[NO VERIFICADO]** RFC 8785 (JCS) define canonicalización de JSON (orden de claves por UTF-16, serialización numérica ES6) para poder firmar JSON "lógico"; no hay implementación en la BCL (existen paquetes de terceros).

### Inferences
- Modelo de amenazas "perfil manipulado": (a) perfil malicioso que escribe fuera de zonas → brick; (b) perfil que cambia `sha256` objetivo para aplicarse a otro firmware; (c) perfil corrupto (descarga incompleta); (d) DoS del parser (JSON gigante/profundo). La firma resuelve (a)-(c) para perfiles de terceros; las comprobaciones de zonas/precondiciones (sección 4) son la defensa en profundidad incluso con perfiles firmados.
- Recomendación concreta:
  - Nivel 1 (mínimo): perfiles oficiales como `EmbeddedResource`, y además una tabla `KnownProfiles { name → SHA-256 }` en código; al cargar, hash de bytes y comparar.
  - Nivel 2: perfiles externos `perfil.json` + `perfil.json.sig` (firma ECDSA P-256/SHA-256 de los bytes exactos, clave pública embebida en el ejecutable). Firmar bytes evita JCS; JCS sólo si se necesita firmar *dentro* del JSON.
  - Perfiles no firmados: modo "desarrollador" explícito con aviso rojo y registro en el informe (`profileTrust: "unsigned"`).
  - Incluir en el informe el SHA-256 del perfil usado.

### Gaps
- No se confirmó el texto de RFC 8785 ni librerías .NET de JCS.

## 4. Enforcement de zonas, solapes y postcondición (diff == conjunto esperado)

### Takeaway
Tratar el firmware como `byte[]` inmutable de entrada; construir un **plan de escritura** (lista de rangos [addr, addr+len)) validado contra zonas permitidas y libre de solapes, aplicar sobre una **copia**, y verificar después con un diff completo que los bytes cambiados ⊆ unión de rangos planificados y que cada rango contiene exactamente lo esperado.

### Cited Findings
- Precedentes: BPS/UPS verifican destino con CRC (postcondición) — [crate ups](https://docs.rs/ups/latest/ups/struct.UpsPatch.html); xdelta verifica origen — [Data Crystal](https://datacrystal.romhacking.net/wiki/Patch). TunerPro no puede excluir áreas del checksum, ilustrando la necesidad de modelar rangos explícitamente — [foro TunerPro](https://www.tunerpro.net/forum3/viewtopic.php?p=6338).

### Inferences
- Algoritmo:
  1. Precondición global: `SHA256(input) == profile.target.sha256` y `input.Length == size`.
  2. Resolver opciones UI → lista de operaciones (patches fijos, celdas de tabla, logo). Cada operación produce `(addr, expectedOld?, newBytes, origen)`.
  3. Validación: `addr + len` sin overflow (usar `long`/`checked`), dentro de `[0,size)`, contenido completo en **alguna** zona permitida (no basta con solapar); ordenar por addr y comprobar `prev.end <= cur.start` (solape = error, salvo que el perfil declare explícitamente grupos mutuamente excluyentes, validados en carga).
  4. Precondición por patch: `input[addr..] == original`; si `== new` → "ya aplicado" (idempotencia); otro → abortar.
  5. Aplicar sobre `byte[] output = input.ToArray()`.
  6. Postcondición: recorrer los 4 MB y construir el conjunto de offsets cambiados; exigir `changed ⊆ planned` y para cada rango `output[r] == newBytes`. Comparar el número de bytes cambiados con lo esperado. Si hay `expectedOutputSha256`, compararlo.
  7. Recalcular checksums internos (si existen) como operación también declarada y dentro de zonas.
- Logos: validar `maxSize` antes de codificar; rellenar el resto del slot con el byte de relleno definido (para determinismo); nunca truncar silenciosamente.
- Tablas editables (keymaps IR): validar cada celda contra un dominio (enum/rango) y unicidad si aplica.
- Para 4 MB, el diff completo en memoria cuesta milisegundos; usar `Span<byte>`/`SequenceEqual` vectorizado.

### Gaps
- No hay fuente específica sobre estrategias de zonas en herramientas MStar; es diseño propio.

## 5. Escrituras atómicas, backups y protección del original en Windows

### Takeaway
Nunca escribir sobre la entrada. Escribir a un temporal **en el mismo directorio/volumen**, `Flush(true)`, verificar releyendo y hasheando, y luego renombrar con `File.Move(tmp, dst, overwrite:true)` (MoveFileEx REPLACE_EXISTING) o `File.Replace` si el destino ya existe y se quiere backup. La atomicidad en NTFS es "práctica" pero no está documentada formalmente.

### Cited Findings
- `MOVEFILE_WRITE_THROUGH` hace que la función no retorne hasta que el archivo se mueve en disco; la documentación no promete atomicidad — [MoveFileEx (Microsoft)](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexa).
- Debate sobre atomicidad: se considera atómico en el mismo volumen si el FS lo soporta; CPython lo listó como no garantizado; en OpenJDK se usaron REPLACE_EXISTING|WRITE_THROUGH para arreglar fallos de ATOMIC_MOVE bajo alta frecuencia — [Atomic File Writes on Windows](https://antonymale.co.uk/windows-atomic-file-writes.html); [bugs.python.org #8828](https://bugs.python.org/issue8828); [OpenJDK nio-dev](https://mail.openjdk.org/pipermail/nio-dev/2023-February/013256.html).
- Go `natefinch/atomic` usa MoveFileEx con esas banderas y promete reemplazo total o nada — [natefinch/atomic](https://beta.pkg.go.dev/github.com/natefinch/atomic). Terraform tiene un `replacefile_windows.go` similar — [terraform replacefile](https://third-party-mirror.googlesource.com/terraform/+/refs/heads/main/v1.2.3/internal/replacefile/replacefile_windows.go).
- `ReplaceFile` sólo funciona si existe el archivo a reemplazar y conserva metadatos del destino — [SO: ¿ReplaceFile sólo conveniencia?](https://kiwix.lema.org/stackoverflow.com_en_all_2023-11/questions/17611093/is-the-replacefile-windows-api-a-convenience-function-only).
- **[NO VERIFICADO]** `File.Move(src,dst,overwrite)` existe desde .NET Core 3.0 y en Windows usa MoveFileEx con REPLACE_EXISTING (sin WRITE_THROUGH); `File.Replace(src,dst,backup)` envuelve ReplaceFile y exige mismo volumen.

### Inferences
- Flujo recomendado:
  1. Abrir la entrada con `FileShare.Read` y `FileAccess.Read`; leer a memoria; hashear. Opcionalmente copiar a `backups\<sha256>.bin` (nombre por contenido = deduplicación) con `File.SetAttributes(ReadOnly)` y un `.sha256` al lado; verificar copia releyendo.
  2. La salida debe tener ruta distinta de la entrada (comparar `Path.GetFullPath` sin distinción de mayúsculas, y además detectar mismo archivo por hard link si se quiere ser estricto **[NO VERIFICADO]** vía `GetFileInformationByHandle`); si coincide, rechazar.
  3. `FileStream` con `FileMode.CreateNew`, `FileOptions.WriteThrough` sobre `dst + ".tmp-<guid>"` en el mismo directorio; `Write`; `Flush(flushToDisk:true)`; cerrar.
  4. Releer el temporal y comprobar SHA-256 == hash calculado en memoria.
  5. Renombrar: si quieres atomicidad máxima, P/Invoke `MoveFileExW(tmp, dst, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)`; si no, `File.Move(tmp, dst, true)`.
  6. En cualquier fallo, borrar el temporal en `finally`. Guardar informe junto a la salida.
- Cuidado con antivirus/indexadores que bloquean el archivo brevemente → reintentos con backoff en el rename (el problema de OpenJDK sugiere fallos transitorios).

### Gaps
- No se pudo abrir la página de `File.Replace`/`File.Move` en learn.microsoft.com para confirmar comportamiento.

## 6. Informes y reproducibilidad de salidas

### Takeaway
Generar dos informes por ejecución: JSON (máquina) y texto/HTML (humano), ambos derivados del mismo modelo. Para reproducibilidad: misma entrada + mismo perfil (hash) + mismas opciones ⇒ mismo SHA-256 de salida; si se empaquetan resultados en ZIP, fijar timestamps y orden de entradas.

### Cited Findings
- ZIP almacena fechas con resolución de 2 s (`ZipArchiveEntry.LastWriteTime`) — [Microsoft docs LastWriteTime](https://learn.microsoft.com/ja-jp/dotnet/api/system.io.compression.ziparchiveentry.lastwritetime).
- NuGet: la hora de modificación es la principal causa de no-determinismo; se fija con `DeterministicTimestamp` o `SOURCE_DATE_EPOCH`; paquetes deterministas por defecto desde .NET 11 — [NuGet deterministic packages](https://learn.microsoft.com/lb-lu/nuget/create-packages/deterministic-packages).
- repro-zipfile usa 1980-01-01 como fecha fija (mínima soportada por ZIP); el orden de entradas debe ser estable — [repro-zipfile (PyPI)](https://pypi.org/project/repro-zipfile); [reproducible-builds.org: Archive metadata](https://reproducible-builds.org/docs/archives/).
- Snapshot testing y rutas: scrubbing para estabilidad — [Meziantou: reproducible builds and snapshot testing](https://www.meziantou.net/reproducible-builds-and-snapshot-testing-handling-file-paths-in-dotnet.htm).

### Inferences
- Contenido mínimo del informe JSON: `toolVersion` (+ commit), `profile {id, version, sha256, trust}`, `input {path, size, sha256}`, `options` (normalizadas y ordenadas), `operations[] {id, address, length, oldHex, newHex, origin}`, `verification {preconditionsOk, changedBytes, unexpectedChanges: [], outputSha256, expectedOutputSha256?}`, `backup {path, sha256}`, `timestampUtc` (único campo no determinista; excluirlo del "hash de informe" o separarlo en `meta`).
- Serializar el informe con orden de propiedades fijo, hex en mayúsculas, `InvariantCulture`, LF; así es comparable con Verify.
- La compresión Deflate de .NET puede variar entre versiones de zlib/runtime **[NO VERIFICADO]**: si el ZIP debe ser bit-idéntico, usar `CompressionLevel.NoCompression` o comparar por hash de contenido, no del contenedor.
- Si el usuario exporta "receta" (perfil hash + opciones + hash entrada), cualquiera puede regenerar la salida y comprobar `outputSha256`.

### Gaps
- No se verificó determinismo de `DeflateStream` entre versiones de .NET.

## 7. Pruebas: snapshots, property-based, fuzzing y fixtures con copyright

### Takeaway
xUnit + **Verify** para informes/planes de parche (snapshots de texto, no de binarios de 4 MB); **CsCheck/FsCheck** para invariantes del motor (no escribir fuera de zonas, idempotencia, revertir == original); **SharpFuzz** (libFuzzer en Windows) para el cargador de perfiles; firmwares reales fuera de git, referenciados por SHA-256 y ruta en variable de entorno, con `Skip` cuando faltan.

### Cited Findings
- Verify: librería de snapshot testing para .NET (Simon Cropp), se integra con xUnit/NUnit/MSTest, compara binarios byte a byte, tiene "scrubbers" para valores dinámicos; recomiendan `.gitattributes` para marcar binarios; los snapshots binarios grandes ensucian el repo — [VerifyTests/Verify](https://github.com/VerifyTests/Verify); [JetBrains blog](https://blog.jetbrains.com/dotnet/2024/07/11/snapshot-testing-in-net-with-verify/); [Duende: Verify sponsorship](https://duendesoftware.com/blog/20260409-verify-open-source-sponsorship).
- SharpFuzz lleva afl-fuzz a .NET; libFuzzer permite soporte nativo en Windows; >80 bugs encontrados, incluidos CVE-2019-0980 y CVE-2019-0981 (DoS en .NET); encuentra también bugs de corrección — [Metalnem/sharpfuzz](https://github.com/metalnem/sharpfuzz); [docs libFuzzer](https://github.com/Metalnem/sharpfuzz/blob/master/docs/libFuzzer.md); [Five years of fuzzing .NET](https://hacklines.com/en/editions/309662/click). Existe WinSharpFuzz para Windows — [NuGet WinSharpFuzz](https://www.nuget.org/packages/WinSharpFuzz).
- **[NO VERIFICADO]** CsCheck (Anthony Lloyd) es una librería PBT C#-first con shrinking y pruebas de concurrencia/regresión; FsCheck tiene integración xUnit (`[Property]`). Git LFS no resuelve el problema legal (los binarios siguen distribuidos).

### Inferences
- Tests concretos:
  - Golden: para cada perfil oficial + combinación de opciones representativa, snapshot del **plan de operaciones** y del informe JSON (Verify, texto) + `outputSha256` esperado almacenado en el snapshot.
  - Firmware sintético: generar imágenes de 4 MB deterministas (PRNG con semilla) e insertar los `original` bytes del perfil en sus direcciones; ajustar el perfil de prueba a su SHA-256. Permite probar todo el pipeline sin material con copyright.
  - Propiedades: (1) `changed ⊆ zones` para cualquier opción/tabla generada; (2) aplicar dos veces = aplicar una vez; (3) revertir(aplicar(x)) == x; (4) perfiles con solapes/fuera de rango siempre rechazados; (5) parser hex nunca lanza excepciones no controladas.
  - Fuzzing: target = `ProfileLoader.Load(byte[])` que sólo puede lanzar `ProfileValidationException`; cualquier otra excepción/stack overflow = bug. Semillas: perfiles reales.
  - Escritura atómica: inyectar fallos (interfaz `IFileSystem`, p. ej. System.IO.Abstractions **[NO VERIFICADO]**) para comprobar que la entrada nunca cambia y no quedan temporales.
- Fixtures reales: `tests/fixtures.manifest.json` en git con `{name, sha256, size, source}`; los binarios en carpeta local (`FIRMWARE_FIXTURES_DIR`) ignorada por `.gitignore`; un `[SkippableFact]`/`Assert.Skip` (xUnit v3) si falta; CI privada puede montar un volumen. Verificar SHA-256 del fixture antes de usarlo.

### Gaps
- No se obtuvo documentación primaria de CsCheck/FsCheck ni de Assert.Skip en xUnit v3 en esta sesión.
- Sin fuentes sobre aspectos legales de distribuir dumps de firmware; consultar asesoría si se compartirán.

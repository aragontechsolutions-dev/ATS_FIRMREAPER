# Empaquetado y distribución de una app WPF (C#) para Windows 10 como un único .exe autocontenido (sin instalación, sin admin, sin internet)

> Fecha de investigación: 15-nov-2026. Restricciones de red de la sesión: learn.microsoft.com, azure.microsoft.com, docs.github.com y todesktop.com **no se pudieron consultar directamente**; se usaron las fuentes Markdown de esas páginas en GitHub (repos `dotnet/docs`, `dotnet/core`, `MicrosoftDocs/azure-docs`, `MicrosoftDocs/windows-dev-docs`, `github/docs`), que son el origen de la documentación publicada. Las afirmaciones marcadas **[NO VERIFICADO]** provienen de conocimiento previo o de fuentes secundarias y deben comprobarse.

## 1. ¿.NET 8 o .NET 10 (u 11)? ¿Funciona en Windows 10 22H2?

### Takeaway
En nov-2026 **.NET 8 ya está fuera de soporte** (terminó el 10-nov-2026) y .NET 11 (STS) acaba de salir; un proyecto nuevo debe apuntar a **.NET 10 (LTS, soporte hasta 14-nov-2028)**. Oficialmente .NET 10 **no lista Windows 10 22H2 (Home/Pro)** como SO soportado (solo ediciones LTSC/IoT de Windows 10), porque Windows 10 22H2 perdió soporte el 14-oct-2025; en la práctica es casi seguro que funciona, pero es "no probado/no soportado" por Microsoft.

### Cited Findings
- Tabla oficial de soporte: .NET 10 = LTS, publicado 11-nov-2025, último parche 10.0.15 (10-nov-2026), **fin de soporte 14-nov-2028**. .NET 11 = STS, publicado 10-nov-2026, fin de soporte 14-nov-2028. — [dotnet.microsoft.com support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- **.NET 8 (LTS) fin de soporte 10-nov-2026**, último parche 8.0.34; .NET 9 (STS) también terminó el 10-nov-2026 (STS extendido a 24 meses). Microsoft advierte: "Your use of out-of-support .NET versions may put your applications... at risk". — [dotnet.microsoft.com support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- Regla: versiones pares = LTS (3 años), impares = STS (2 años). — [mismo](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- `supported-os.md` de .NET 10 (actualizado 2026/09/28) lista para Windows cliente: 11 26H1, 11 25H2, 11 24H2 (IoT/E), 11 23H2 (E), **10 21H2 (E) [LTSC 2021], 10 21H2 (IoT), 10 1809 (E) [LTSC 2019], 10 1607 (E) [LTSB 2016]**; arquitecturas Arm64/x64/x86. **Windows 10 22H2 no aparece.** — [dotnet/core supported-os.md 10.0](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) (verificado también en el JSON: `supported-versions` = `["11-26h1-e", ..., "10-21h2-e-lts", "10-21h2-iot-lts", "10-1809-e-lts", "10-1607-e-lts"]`, [supported-os.json](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/supported-os.json))
- La misma página: "OS versions that are out of support by the OS publisher are not tested or supported by .NET." — [supported-os.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- Política de ciclo de vida de SO: "Support is typically removed when an operating system goes out of mainline support"; a veces se ha mantenido soporte más tiempo, sin mencionar ESU de Windows 10. — [dotnet/core os-lifecycle-policy.md](https://github.com/dotnet/core/blob/main/os-lifecycle-policy.md)
- Una versión antigua del PR que creó la lista de .NET 10 incluía "10-22h2" (antes de oct-2025); hoy ya no está. — [mirror del PR dotnet/core #9647](https://github.yxlr123.workers.dev/dotnet/core/pull/9647/files) (mirror de terceros; tómese como indicio)
- Windows 10 terminó soporte el 14-oct-2025; existe ESU. Para consumidores, ESU requiere Windows 10 22H2 (Home/Pro/Pro Education/Workstations); fuentes secundarias indican ESU de consumo extendido hasta 12-oct-2027 [NO VERIFICADO en fuente Microsoft]. — [support.microsoft.com KB 5072653](https://support.microsoft.com/en-us/help/5072653); [TechRepublic](https://www.techrepublic.com/article/news-windows-10-esu-eligibility/)

### Inferences
- Windows 10 22H2 (build 19045) comparte base de código y servicio con LTSC 2021 (21H2, build 19044), que **sí** está soportado por .NET 10. Por tanto, una app WPF .NET 10 autocontenida debería funcionar sin problemas en 22H2; el riesgo es solo "formal" (Microsoft no lo prueba ni atiende bugs específicos).
- Recomendación: **apuntar a `net10.0-windows`**. No usar .NET 8 (ya sin parches) ni .NET 11 (STS, y su lista de SO puede ser aún más restrictiva). Como la app es autocontenida, el runtime va dentro del .exe: hay que **republicar** para incorporar parches de seguridad (no se actualiza vía Windows Update).
- Si se quiere compatibilidad máxima con equipos Windows 10 muy antiguos (p. ej. 1507–1909 sin actualizar, frecuentes en talleres sin internet), probar en VM: .NET moderno requiere Windows 10 1607+ como mínimo práctico [NO VERIFICADO para .NET 10 en 22H2 no-LTSC].

### Gaps
- No se encontró anuncio explícito del equipo .NET sobre retirar Windows 10 22H2 (solo el cambio en la tabla). No se encontró la lista de SO de .NET 11 para Windows 10.

## 2. Publicación WPF single-file autocontenida: propiedades, tamaño, trimming, ReadyToRun, Native AOT

### Takeaway
Se obtiene un único .exe con `PublishSingleFile=true`, `SelfContained=true`, `RuntimeIdentifier=win-x64` e `IncludeNativeLibrariesForSelfExtract=true`; las DLL nativas de WPF (wpfgfx, PresentationNative, etc.) **se extraen en `%TEMP%\.net`** al arrancar (no requiere admin). **Trimming está deshabilitado para WPF** por el SDK y **Native AOT no es viable para WPF**, así que el exe ronda decenas de MB (comprimido ~60–70 MB, sin comprimir ~150 MB **[NO VERIFICADO, medir]**).

### Cited Findings
- `PublishSingleFile` habilita single-file y sus advertencias; `RuntimeIdentifier` fija SO/CPU y pone `SelfContained` a true por defecto: "Single file apps are always OS and architecture specific." — [dotnet/docs single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)
- Solo se empaquetan DLL gestionadas (se cargan desde memoria); las nativas quedan sueltas **salvo** `IncludeNativeLibrariesForSelfExtract=true`, que las incrusta y extrae. `IncludeAllContentForSelfExtract` extrae todo: "This mode is not recommended." — [single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)
- Ubicación de extracción: `DOTNET_BUNDLE_EXTRACT_BASE_DIR` si está definida; si no, en Windows `%TEMP%/.net`. Advierte que el directorio no debe ser escribible por usuarios con distintos privilegios. — [single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)
- Sin el flag, WPF deja junto al exe `wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, etc. — [StackOverflow 71209803 (mirror)](https://kaweah.freedombox.rocks/kiwix/content/stackoverflow.com_en_all_2023-11/questions/71209803/how-to-build-a-single-exe-file-with-static-net-runtime-from-a-c-wpf-project); carpeta `%temp%\.net` observada en apps WPF single-file — [Microsoft Q&A 2241031](https://learn.microsoft.com/en-us/answers/questions/2241031/understanding-temp-net-folder-wpf-single-file-self)
- `EnableCompressionInSingleFile=true` comprime ensamblados incrustados; reduce tamaño pero añade coste de arranque (descompresión en memoria); medir. — [single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)
- APIs incompatibles: `Assembly.Location` devuelve cadena vacía; `Assembly.CodeBase` lanza `PlatformNotSupportedException`. Usar `AppContext.BaseDirectory`, `Environment.ProcessPath` y recursos incrustados. `DebugType=embedded` para PDB dentro. C++/CLI no encaja bien. — [single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)
- Single-file se puede combinar con ReadyToRun y trimming. — [single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)
- Trimming: "almost no WPF apps are runnable after trimming, so trimming support for WPF is currently disabled in the .NET SDK"; WinForms también deshabilitado (dependencia de COM marshalling). — [dotnet/docs trimming incompatibilities](https://github.com/dotnet/docs/blob/main/docs/core/deploying/trimming/incompatibilities.md)
- Native AOT: requiere trimming, sin COM integrado en Windows, sin `Reflection.Emit` ni carga dinámica. — [dotnet/docs native-aot](https://github.com/dotnet/docs/blob/main/docs/core/deploying/native-aot/index.md) (la página no menciona WPF; la incompatibilidad se infiere de que WPF no admite trimming).
- Habilitar `PublishTrimmed` en una app WPF provocó cierre en tiempo de ejecución; el fix fue quitar trimming y añadir extracción nativa. — [GeneralUpdate.Tools PR #30](https://github.com/GeneralLibrary/GeneralUpdate.Tools/pull/30)

### Configuración recomendada (.csproj)
```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>false</EnableCompressionInSingleFile> <!-- ver sección antivirus -->
  <PublishReadyToRun>true</PublishReadyToRun>   <!-- arranque más rápido, exe más grande -->
  <DebugType>embedded</DebugType>
  <InvariantGlobalization>false</InvariantGlobalization>
  <SatelliteResourceLanguages>es;en</SatelliteResourceLanguages> <!-- recorta recursos satélite -->
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```
`dotnet publish -c Release` produce `bin\Release\net10.0-windows\win-x64\publish\App.exe`.

### Inferences
- `%TEMP%` es por usuario → la extracción no requiere admin. Riesgos: políticas AppLocker/SRP que bloquean ejecución de DLL desde `%TEMP%` en equipos corporativos, y limpiadores que borran `%TEMP%\.net` (se re-extrae al siguiente arranque).
- `SatelliteResourceLanguages` es útil porque sin trimming el exe incluye recursos de WPF en ~13 idiomas [NO VERIFICADO el ahorro exacto].
- Elegir `win-x64` (Windows 10 x86 de 32 bits todavía existe en equipos viejos; si se necesita, publicar también `win-x86`).
- ReadyToRun vs. compresión: ambos son ajustes de tamaño/arranque; medir en la máquina más lenta objetivo.

### Gaps
- Tamaño exacto del exe WPF .NET 10 con/sin compresión: no hay cifra oficial; medir.
- Confirmación oficial explícita "Native AOT no soporta WPF" no se obtuvo en esta sesión (learn.microsoft.com bloqueado); es consenso conocido [NO VERIFICADO aquí].

## 3. Alternativas: WinForms, Avalonia, WinUI 3; MVVM (CommunityToolkit.Mvvm)

### Takeaway
Para una herramienta de ingeniería pequeña, solo Windows, offline y en un único exe, **WPF (o WinForms) en .NET 10 sigue siendo la opción más simple**. Avalonia es la alternativa si se quiere Native AOT (exe más pequeño, arranque rápido) o Linux; WinUI 3 es la peor opción para "un solo exe sin instalación".

### Cited Findings
- WinForms tampoco admite trimming (SDK deshabilitado). — [dotnet/docs trimming incompatibilities](https://github.com/dotnet/docs/blob/main/docs/core/deploying/trimming/incompatibilities.md)
- Existe control hex para Avalonia (`AvaloniaHex` en NuGet). — [nuget AvaloniaHex](https://www-1.nuget.org/packages/AvaloniaHex)

### Inferences / conocimiento previo [NO VERIFICADO en esta sesión]
- **WinForms**: mismo empaquetado single-file que WPF, exe algo menor, diseñador maduro, control hex MIT disponible (Be.HexEditor/HexBox). Peor soporte de DPI/estilos que WPF; MVVM menos natural.
- **WPF**: XAML, binding y MVVM nativos, buen escalado DPI vectorial; Windows-only; sin trimming/AOT → exe grande. Microsoft lo mantiene open source (dotnet/wpf) con mejoras menores (tema Fluent en .NET 9+).
- **Avalonia 11.x**: XAML similar a WPF, multiplataforma, admite trimming y Native AOT (exe nativo de ~decenas de MB o menos, sin extracción a %TEMP%, potencialmente menos falsos positivos). Curva de aprendizaje si se viene de WPF; licencia MIT del núcleo (ciertas herramientas/"Avalonia XPF" son comerciales).
- **WinUI 3 / Windows App SDK**: orientado a MSIX; el modo "unpackaged self-contained" existe pero el single-file es frágil/no soportado oficialmente; requiere Windows 10 1809+. No recomendado para este caso.
- **CommunityToolkit.Mvvm** (MIT, .NET Foundation): `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` mediante source generators; compatible con trimming/AOT y con WPF/Avalonia/WinForms. Recomendado sobre Prism/MVVM Light (este último abandonado).

### Gaps
- No se verificaron versiones actuales (nov-2026) de Avalonia, CommunityToolkit.Mvvm ni el estado single-file de Windows App SDK.

## 4. Firma de código, SmartScreen y falsos positivos de antivirus

### Takeaway
**Azure Artifact Signing (ex Trusted Signing) cuesta ~9,99 USD/mes pero solo está disponible para individuos en EE. UU. y Canadá**; para un desarrollador en otro país la alternativa es un certificado OV (~150–300 USD/año). **Desde 2024, EV ya no salta SmartScreen**: OV, EV y Artifact Signing construyen reputación igual, por hash de archivo. Un exe sin firmar mostrará "Windows protegió su PC" (Más información → Ejecutar de todas formas) y es más propenso a detecciones heurísticas tipo `Wacatac.B!ml`.

### Cited Findings
- Tabla oficial de Microsoft: Artifact Signing **~$9.99/mes**; "Organizations: USA, Canada, EU, UK. Individuals: USA and Canada only"; "Reputation builds over time; initial warnings expected". OV **$150–300/año**, mundial. EV **$400+/año**, "Same as OV since 2024 — no longer instant bypass". Sin firma: "Strong SmartScreen block; enterprises may block entirely". — [MicrosoftDocs windows-dev-docs code-signing-options.md](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/package-and-deploy/code-signing-options.md) (publicado en learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
- "Extended Validation (EV) certificates previously bypassed SmartScreen entirely on first download... **That behavior was removed in 2024.**" Pagar EV solo por SmartScreen "is no longer justified". — [mismo](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/package-and-deploy/code-signing-options.md)
- Fecha del cambio EV: el programa Trusted Root indicó retirar OIDs EV de raíces a partir de **agosto 2024**; SSL.com cita marzo 2024 (conflicto de fechas). — [Microsoft Q&A 1846647](https://learn.microsoft.com/en-us/answers/questions/1846647/program-requirements-microsoft-trusted-root-progra)
- Quickstart actual de Artifact Signing (más reciente que la tabla anterior): Public Trust para **organizaciones** en EE. UU., Canadá, UE, Reino Unido, Australia, Nueva Zelanda, Japón, Corea del Sur, Singapur, Suiza, Noruega e Israel; "**Individual developers must be located in the United States or Canada**". La cuenta de facturación Azure debe ser tipo Individual y coincidir con el documento de identidad. — [MicrosoftDocs azure-docs artifact-signing/quickstart.md](https://github.com/MicrosoftDocs/azure-docs/blob/main/articles/artifact-signing/quickstart.md) (conflicto menor con la tabla de windows-dev-docs, que lista menos países para organizaciones)
- Q&A de marzo 2026: tenants fuera de geografías soportadas no pueden enviar validación de identidad individual; sin ETA para otros países. — [Microsoft Q&A 5810735](https://learn.microsoft.com/en-us/answers/questions/5810735/cant-create-a-new-trusted-signing-individual-ident); [Q&A 2243504](https://learn.microsoft.com/en-us/answers/questions/2243504/trusted-signing-for-other-countries)
- Renovación de certificado EV reinicia reputación SmartScreen (caso reportado). — [Microsoft Q&A 5900208](https://learn.microsoft.com/en-us/answers/questions/5900208/smartscreen-reputation-reset-following-ev-certific)
- Falsos positivos: Defender marcó `Trojan:Script/Wacatac.B!ml` en binarios .NET 9 AOT **comprimidos en ZIP**. — [Dylan Beattie, dic-2024](https://dylanbeattie.net/2024/12/09/dotnet-9-aot-zip-file-windows-defender-false-positives.html); casos similares en apps .NET autocontenidas y WinForms VB.NET — [Advanced Installer forum](https://www.advancedinstaller.com/forums/viewtopic.php?p=130789), [Microsoft Q&A 1622364](https://learn.microsoft.com/en-in/answers/questions/1622364/vb-net-desktop-forms-app-trojan-win32-wacatac-b-ml)
- Vía de reclamación: portal de envío de Microsoft Security Intelligence (WDSI, microsoft.com/wdsi/filesubmission); un desarrollador reportó ~5 h de procesamiento. Hay reportes de fallos en la página de detalle de envíos (2026). — [Advanced Installer forum](https://www.advancedinstaller.com/forums/viewtopic.php?p=130778); [Tech Community 4530787](https://techcommunity.microsoft.com/discussions/microsoftdefenderatp/microsoft-defender-false-positive-and-wdsi-submission-details-page-bug/4530787)
- Artifact Signing ha sido abusado para firmar malware (lo que indica que la firma no garantiza ausencia de detección). — [BleepingComputer](https://bleepingcomputer.com/news/security/microsoft-trusted-signing-service-abused-to-code-sign-malware)

### Inferences
- **Para un desarrollador en Cuba**: Artifact Signing no es accesible (individuos solo EE. UU./Canadá; además pago Azure con tarjeta desde Cuba es improbable). CAs comerciales (DigiCert, Sectigo, SSL.com — empresas de EE. UU./Reino Unido) muy probablemente **no emitirán** certificados a residentes en Cuba por el embargo [NO VERIFICADO; inferencia]. Opciones realistas: (a) distribuir **sin firmar** e instruir al usuario ("Más información → Ejecutar de todas formas", o Propiedades → "Desbloquear"); (b) que un socio/empresa en un país elegible firme; (c) para uso interno, certificado autofirmado instalado manualmente en "Editores de confianza".
- **Mark-of-the-Web**: SmartScreen solo actúa si el archivo tiene la marca de zona (descargado por navegador/correo). Copiado por USB/red local sin MOTW, un exe sin firmar **no muestra SmartScreen** (sí puede activar Defender heurístico). Relevante en entornos sin internet. [NO VERIFICADO con fuente en esta sesión; comportamiento conocido de Windows]
- Reducir falsos positivos: no comprimir el single-file (`EnableCompressionInSingleFile=false`) y no empaquetarlo con UPX ni SFX de terceros; incluir metadatos completos (`AssemblyTitle`, `Company`, `Product`, `FileVersion`, icono); mantener nombre de archivo y editor estables; distribuir el exe tal cual o en ZIP estándar (comprobar el ZIP también); firmar si es posible; enviar cada versión al portal WDSI como "software developer" antes de distribuir; comprobar en VirusTotal. La relación "compresión → más falsos positivos" es consenso comunitario, **no documentado por Microsoft** [NO VERIFICADO].
- Una herramienta que parchea firmware (lee/escribe binarios, posiblemente accede a puertos serie/USB) es heurísticamente más "sospechosa"; esperar detecciones esporádicas.
- Desde jun-2023 la clave privada de certificados OV debe estar en hardware (token/HSM), lo que encarece y complica CI [NO VERIFICADO aquí; requisito CA/B Forum conocido].

### Gaps
- No se verificó la página de precios de Azure (bloqueada); el ~$9,99/mes viene de la doc de Windows. Premium (~$99,99/mes) [NO VERIFICADO].
- No se encontró política explícita de CAs comerciales respecto a solicitantes en Cuba.

## 5. Controles de vista/edición hexadecimal

### Takeaway
**Atención a licencias**: el control WPF más conocido, **WPFHexaEditor, es ahora AGPL-3.0** (obliga a publicar el código fuente de la app si se distribuye). Para código cerrado, la opción más segura es **Be.HexEditor/HexBox (MIT, WinForms)** alojado en WPF vía `WindowsFormsHost`, o escribir un visor hex propio (ItemsControl virtualizado / render personalizado), que para "solo ver + parchear bytes" es poco trabajo.

### Cited Findings
- WpfHexEditorControl (abbaye): "100% free and open source (GNU AGPL v3.0)"; paquetes NuGet `WPFHexaEditor`, `WpfHexEditor.Core.ByteProvider`; targets net8.0-windows; modos inserción/sobrescritura, tablas TBL; "Active Development", README revisado 2026-05-18. — [GitHub abbaye/WpfHexEditorControl](https://github.com/abbaye/WpfHexEditorControl)
- NuGet `WPFHexaEditor`: licencia AGPL-3.0; 3.4.5 (18-jun-2026); compatible net8.0-windows y computado net9/net10; 3.0.0 pasó a solo .NET 8; versiones anteriores 2.1.7 (2021) y 2.2.0 (feb-2026). — [nuget.org WPFHexaEditor](https://www.nuget.org/packages/WPFHexaEditor)
- Las versiones 2.x anteriores se distribuían bajo **Apache 2.0** [NO VERIFICADO — la página NuGet solo muestra la licencia actual; revisar `LICENSE` en el historial del repo antes de usar 2.1.7].
- NuGet `HexBox` 2.0.42: control WinForms para ver/editar binarios en hex, **MIT**, targets .NET Framework 4.8, .NET 8 (Windows) y .NET 10. — [nuget.org HexBox](https://www.nuget.org/packages/HexBox); repo [bernharde/hexbox](https://github.com/bernharde/hexbox)
- Be.HexEditor original (SourceForge): MIT, "free for personal and commercial use". — [SourceForge hexbox](https://sourceforge.net/projects/hexbox/); paquete `Be.Windows.Forms.HexBox.Net5` 1.8.0 — [nuget](https://www.nuget.org/packages/Be.Windows.Forms.HexBox.Net5)
- Avalonia: `AvaloniaHex`. — [nuget](https://www-1.nuget.org/packages/AvaloniaHex)

### Inferences
- `WindowsFormsHost` en WPF tiene problemas de "airspace" (el control WinForms siempre se dibuja encima) y escalado DPI distinto; aceptable para una herramienta simple.
- Para firmwares de TV MStar (típicamente 4–64 MB, a veces más con particiones), un visor propio con `VirtualizingStackPanel` y lectura por bloques vía `MemoryMappedFile` o `FileStream` es suficiente y evita dependencias.

### Gaps
- No se verificó el soporte de archivos grandes de cada control.

## 6. Localización (UI en español), High-DPI y ajustes en %LOCALAPPDATA%

### Takeaway
Para una UI solo en español basta con escribir los textos en XAML/`.resx` neutro en español; si se quieren varios idiomas, `.resx` + `SatelliteResourceLanguages`. WPF es per-monitor DPI aware declarándolo en `app.manifest`. Guardar configuración en `%LOCALAPPDATA%\<App>` (o junto al exe si es "portable" y la carpeta es escribible) sin requerir admin.

### Cited Findings
- `Environment.ProcessPath` / `AppContext.BaseDirectory` para ubicar el exe en single-file (no `Assembly.Location`). — [single-file overview](https://github.com/dotnet/docs/blob/main/docs/core/deploying/single-file/overview.md)

### Inferences / conocimiento previo [NO VERIFICADO en esta sesión]
- **Localización**: archivos `Strings.resx` (neutro) + `Strings.es.resx`; en single-file los ensamblados satélite se incrustan en el bundle. Fijar `CultureInfo.DefaultThreadCurrentUICulture = new("es-ES")` si se quiere forzar español independientemente del idioma de Windows. Cuidado con `InvariantGlobalization=true` (rompe formatos de número/fecha localizados). Usar `CultureInfo.InvariantCulture` al parsear hex/números de archivos de configuración.
- **High-DPI**: en `app.manifest` declarar `<dpiAwareness>PerMonitorV2</dpiAwareness>`; WPF en .NET Core ya es per-monitor aware por defecto [NO VERIFICADO]. Usar fuentes vectoriales/iconos SVG o `DrawingImage`; fuente monoespaciada para hex (Consolas está presente en Windows 10).
- **Ajustes sin admin**: `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` → `C:\Users\<u>\AppData\Local\<App>\settings.json` con `System.Text.Json`. Evitar `Properties.Settings` (ruta con hash, rara en single-file) y nunca escribir en `Program Files` ni HKLM. Modo "portable": si existe `portable.txt` junto al exe y la carpeta es escribible, guardar ahí.
- No pedir elevación: el manifiesto debe tener `requestedExecutionLevel level="asInvoker"`. Si se necesita acceso a drivers USB/serie que requieran instalación, eso rompe el requisito "sin admin" — documentarlo.

### Gaps
- No se consultó documentación oficial de WPF sobre DPI/localización (learn.microsoft.com bloqueado en la sesión).

## 7. Desarrollo y compilación con internet restringida (p. ej. Cuba)

### Takeaway
GitHub (incluidos servicios de pago y Actions) está **disponible para desarrolladores en Cuba** según la política de controles comerciales de GitHub, así que **compilar en GitHub Actions (runner `windows-latest`) y descargar el exe como artefacto** es una vía viable cuando descargar el SDK/NuGet localmente es difícil. Para trabajo local offline: SDK .NET en zip/instalador descargado una vez + carpeta de paquetes NuGet (caché global o feed local).

### Cited Findings
- GitHub: "GitHub cloud services, both free and paid, are also generally available to developers located in Cuba." GitHub Enterprise Server y **Copilot** no pueden venderse/exportarse a Cuba (Country Group E:1). — [github/docs github-and-trade-controls.md](https://github.com/github/docs/blob/main/content/site-policy/other-site-policies/github-and-trade-controls.md)
- Restricciones de 2019 a Cuba (cuentas privadas/pago) ya superadas según la política actual. — [Windows Central 2019](https://www.windowscentral.com/github-developers-restricted-crimea-cuba-iran-and-other-countries-under-us-sanctions); [github/docs](https://github.com/github/docs/blob/main/content/site-policy/other-site-policies/github-and-trade-controls.md)
- Estudio USENIX Security 2024: muchos servicios aplican geobloqueo a Cuba sin explicación (tecnología, negocios, finanzas). — [USENIX Security 2024](https://usenix.org/system/files/usenixsecurity24-ablove.pdf)
- No se encontró evidencia de que dotnet.microsoft.com o NuGet bloqueen a Cuba. Los dominios antiguos `dotnetcli.azureedge.net` / `dotnetbuilds.azureedge.net` fueron retirados (afecta a scripts `dotnet-install` antiguos). — [heise](https://heise.de/-10224386)

### Inferences / procedimiento offline [pasos conocidos, NO VERIFICADOS en esta sesión]
- **SDK offline**: descargar en un equipo con internet el instalador o el **zip** del SDK .NET 10 (`dotnet-sdk-10.0.x-win-x64.zip`); el zip se descomprime en cualquier carpeta y se usa sin admin (añadir a `PATH` y `DOTNET_ROOT`). Incluye los targeting packs de WPF/WinForms.
- **Runtime packs para self-contained**: publicar `-r win-x64 --self-contained` necesita los paquetes `Microsoft.NETCore.App.Runtime.win-x64`, `Microsoft.WindowsDesktop.App.Runtime.win-x64`, `Microsoft.AspNetCore.App.Runtime.win-x64` (este último a veces) y, con ReadyToRun, `Microsoft.NETCore.App.Crossgen2.win-x64` — **se descargan de NuGet**, no vienen en el SDK. Hacer un `dotnet publish` completo una vez con internet y copiar `%USERPROFILE%\.nuget\packages` al equipo offline, o crear un feed local (carpeta con `.nupkg`) y un `nuget.config` con `<clear/>` + `<add key="local" value="D:\nuget-offline" />`.
- Fijar versiones exactas (`global.json` para SDK; `Directory.Packages.props` para paquetes) para que el feed offline sea reproducible.
- **Visual Studio** offline: crear layout con `vs_community.exe --layout` en un equipo con internet [requiere licencia/registro; VS Community requiere cuenta Microsoft, posible geobloqueo — NO VERIFICADO]. Alternativa ligera: VS Code + C# Dev Kit, o JetBrains Rider (JetBrains no vende a Cuba/sancionados [NO VERIFICADO]).
- **GitHub Actions**: workflow con `actions/setup-dotnet` (`dotnet-version: 10.0.x`) en `windows-latest`, `dotnet publish -c Release -r win-x64`, `actions/upload-artifact`. Ventaja: builds reproducibles, firma en CI si algún día se dispone de certificado. Desventaja: requiere conexión para subir código y bajar artefactos (~60–150 MB por build). Los minutos gratuitos de Actions en repos públicos son ilimitados; en repos privados hay cuota mensual [NO VERIFICADO cuota exacta 2026].

### Gaps
- No se verificó si NuGet.org, dotnet.microsoft.com o Visual Studio aplican geobloqueo efectivo desde IPs cubanas (no hay reportes; sí hay geobloqueo documentado en otros servicios).
- No se verificó la cuota actual de minutos de Actions para cuentas gratuitas.

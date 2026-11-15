using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.Patching;
using FirmReaper.Core.Profiles;
using FirmReaper.Core.Reporting;
using Microsoft.Win32;

namespace FirmReaper.App;

/// <summary>
/// Ventana principal: abrir un .BIN, identificarlo y verificarlo; si es un original con perfil,
/// generar la versión modificada (pestaña "Generar"). El archivo original nunca se modifica.
/// </summary>
public partial class MainWindow : Window
{
    private readonly FirmwareCatalog _catalog;
    private readonly ProfileRegistry _profiles;
    private string? _reportText;
    private string? _reportJson;
    private string? _lastFile;
    private string? _lastSha;
    private readonly Dictionary<string, ComboBox> _optionBoxes = [];
    private CancellationTokenSource? _cts;
    /// <summary>Tabla del mando editada (null = la del perfil).</summary>
    private List<KeyAssignment>? _keyMap;

    public MainWindow()
    {
        InitializeComponent();
        _catalog = FirmwareCatalog.LoadEmbedded();
        _profiles = ProfileRegistry.Load(ProfileRegistry.DefaultExternalFolder());
        if (_profiles.Errors.Count > 0)
        {
            MessageBox.Show(this, "Se ignoraron perfiles externos no válidos:\n\n" + string.Join("\n", _profiles.Errors),
                "FirmReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------ Abrir y analizar

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Abrir volcado SPI (.BIN)",
            Filter = "Volcados SPI (*.bin)|*.bin|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
            _ = AnalyzeAsync(dlg.FileName);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _cts is null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_cts is null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            _ = AnalyzeAsync(files[0]);
    }

    private async Task AnalyzeAsync(string path)
    {
        SetBusy(true, "Analizando…");
        try
        {
            // El análisis (CRC + descompresión de ~4 MB) se hace fuera del hilo de la interfaz.
            var (analysis, id) = await Task.Run(() =>
            {
                var a = FirmwareAnalyzer.AnalyzeFile(path);
                return (a, Identifier.Identify(a, _catalog));
            });
            Show(analysis, id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "No se pudo leer el archivo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, "");
        }
    }

    private void Show(FirmwareAnalysis a, Identification id)
    {
        _lastFile = a.FilePath;
        _lastSha = a.Sha256;
        _reportText = AnalysisReport.ToText(a, id);
        _reportJson = AnalysisReport.ToJson(a, id);

        TxtIdent.Text = id.Message;
        TxtFile.Text = $"{a.FilePath}\nSHA-256: {a.Sha256}" +
                       (a.BoardId is null ? "" : $"   ·   Placa: {a.BoardId}") +
                       (a.SbootTag is null ? "" : $"   ·   {a.SbootTag}");
        Banner.Background = new SolidColorBrush(id.Kind switch
        {
            IdentificationKind.Conocido when a.ContainerValid => Color.FromRgb(0xD8, 0xF0, 0xD8),
            IdentificationKind.DesconocidoValido => Color.FromRgb(0xFF, 0xF4, 0xCC),
            _ => Color.FromRgb(0xFF, 0xD9, 0xD9),
        });

        GridChecks.ItemsSource = a.Checks;
        GridTel.ItemsSource = a.Tel?.Entries;
        TxtReport.Text = _reportText;
        BtnSave.IsEnabled = BtnCopy.IsEnabled = true;

        // Perfiles aplicables: solo a un ORIGINAL con contenedor válido.
        var list = a.ContainerValid ? _profiles.ForOriginal(a.Sha256) : [];
        TxtLog.Clear();
        LstChanges.ItemsSource = null;
        RtbHex.Document = new System.Windows.Documents.FlowDocument();
        if (list.Count == 0)
        {
            PanelProfile.Visibility = Visibility.Collapsed;
            TxtNoProfile.Visibility = Visibility.Visible;
            TxtNoProfile.Text = a.ContainerValid
                ? "No hay modificaciones disponibles para este archivo (solo se modifican originales con perfil)."
                : "El contenedor no es válido: no se puede modificar.";
        }
        else
        {
            TxtNoProfile.Visibility = Visibility.Collapsed;
            PanelProfile.Visibility = Visibility.Visible;
            CmbProfile.ItemsSource = list;
            CmbProfile.SelectedIndex = 0;
        }
    }

    // ------------------------------------------------------------------ Perfil y opciones

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        PanelOptions.Children.Clear();
        _optionBoxes.Clear();
        _keyMap = null;
        if (CmbProfile.SelectedItem is not Profile p) return;

        TxtProfileDesc.Text = p.Description + $"  (v{p.Version}, {(p.Source == ProfileSource.Integrado ? "integrado" : "externo")})";
        TxtProfileWarn.Text = p.Verified ? "" : "ATENCIÓN: perfil SIN VERIFICAR con el binario real. Revisa el informe antes de grabar.";

        foreach (var o in p.Options)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = o.Label + ":", Width = 120, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var cmb = new ComboBox { MinWidth = 320, DisplayMemberPath = nameof(OptionValue.Label), ItemsSource = o.Values };
            cmb.SelectedItem = o.Values.First(v => v.Id == o.Default);
            row.Children.Add(cmb);
            PanelOptions.Children.Add(row);
            _optionBoxes[o.Id] = cmb;
            cmb.SelectionChanged += (_, _) => UpdateKeyMapPanel();
        }
        UpdateKeyMapPanel();
        if (p.Options.Count == 0)
            PanelOptions.Children.Add(new TextBlock { Text = "Este perfil no tiene opciones.", Margin = new Thickness(120, 0, 0, 6), Foreground = Brushes.Gray });
    }

    // ------------------------------------------------------------------ Generar

    private async void OnGenerate(object sender, RoutedEventArgs e)
    {
        if (CmbProfile.SelectedItem is not Profile p || _lastFile is null || _lastSha is null) return;
        var selection = CurrentSelection();

        // Logo propio: se pide el JPEG antes de la carpeta de salida.
        byte[]? customLogo = null;
        if (p.LogoActionFor(selection) == LogoAction.Propio)
        {
            var ofd = new OpenFileDialog
            {
                Title = $"Logo propio (JPEG baseline {p.Logo!.Width}x{p.Logo.Height}, máx. {p.Logo.JpegLength:N0} bytes)",
                Filter = "Imagen JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg",
                CheckFileExists = true,
            };
            if (ofd.ShowDialog(this) != true) return;
            if (new FileInfo(ofd.FileName).Length > FirmwareBuilder.MaxCustomLogoBytes)
            {
                MessageBox.Show(this, "El archivo es demasiado grande para ser el logo.", "FirmReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            customLogo = File.ReadAllBytes(ofd.FileName);
        }

        // Tabla del mando editada: solo si la opción que la contiene está elegida.
        var keyMapToUse = KeyMapApplies(p, selection) ? _keyMap : null;

        var dlg = new OpenFolderDialog
        {
            Title = "Carpeta donde guardar el firmware generado",
            InitialDirectory = Path.GetDirectoryName(_lastFile),
        };
        if (dlg.ShowDialog(this) != true) return;
        string outDir = dlg.FolderName;
        string input = _lastFile;

        TxtLog.Clear();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<string>(Log);
        SetBusy(true, "Generando…");
        try
        {
            var saved = await Task.Run(() =>
            {
                var fw = File.ReadAllBytes(input);
                var r = FirmwareBuilder.Build(fw, p, selection, customLogo, progress, token, keyMapToUse);
                token.ThrowIfCancellationRequested();
                return (r, OutputWriter.Save(r, input, outDir, DateTimeOffset.Now));
            }, token);
            var (result, files) = saved;
            ShowChanges(result);
            Log("");
            Log("LISTO.");
            Log("Firmware: " + files.Firmware);
            Log("SHA-256:  " + result.OutputSha256);
            if (result.Known is not null)
                Log(result.MatchesKnown ? $"Idéntico a la versión documentada {result.Known.FileName}." : $"AVISO: distinto de {result.Known.FileName}.");
            Log("Informe:  " + files.ReportText);
            Log("Copia del original: " + files.Backup);
            MessageBox.Show(this, $"Firmware generado y verificado:\n\n{files.Firmware}\n\nSHA-256: {result.OutputSha256}",
                "FirmReaper", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Cancelado. No se ha guardado nada.");
        }
        catch (Exception ex) when (ex is BuildException or IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Log("ERROR: " + ex.Message);
            Log("No se ha guardado ningún firmware.");
            MessageBox.Show(this, ex.Message, "No se generó el firmware", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false, "");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ------------------------------------------------------------------ Tabla del mando

    private static bool KeyMapApplies(Profile p, IReadOnlyDictionary<string, string> selection) =>
        p.KeyTable is { } kt && selection.TryGetValue(kt.OptionId, out var v) && v == kt.ValueId;

    private Dictionary<string, string> CurrentSelection() =>
        _optionBoxes.ToDictionary(kv => kv.Key, kv => ((OptionValue)kv.Value.SelectedItem).Id);

    private void UpdateKeyMapPanel()
    {
        if (CmbProfile.SelectedItem is not Profile p || p.KeyTable is not { } kt)
        {
            PanelKeyMap.Visibility = Visibility.Collapsed;
            return;
        }
        PanelKeyMap.Visibility = Visibility.Visible;
        bool applies = KeyMapApplies(p, CurrentSelection());
        BtnKeyMap.IsEnabled = applies;
        var map = _keyMap ?? kt.Default;
        TxtKeyMap.Text = !applies
            ? "(no se aplica con estas opciones)"
            : _keyMap is null || kt.IsDefault(_keyMap)
                ? $"la del perfil ({map.Count} teclas)"
                : $"EDITADA ({map.Count} teclas)";
    }

    private void OnEditKeyMap(object sender, RoutedEventArgs e)
    {
        if (CmbProfile.SelectedItem is not Profile p || p.KeyTable is not { } kt) return;
        var w = new KeyMapWindow(p, _keyMap ?? kt.Default) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null)
        {
            _keyMap = kt.IsDefault(w.Result) ? null : w.Result;
            UpdateKeyMapPanel();
        }
    }

    // ------------------------------------------------------------------ Visor hexadecimal de cambios

    private sealed record ChangeItem(string Label, uint Address, byte[] Original, byte[] New);

    private void ShowChanges(BuildResult r)
    {
        var items = r.Patches
            .Select(p => new ChangeItem($"0x{p.Address:X6}  {p.Original.Length,4} B  {p.Description}", p.Address, p.Original, p.New))
            .ToList();
        LstChanges.ItemsSource = items;
        TxtChangesInfo.Text = $"{r.Patches.Count} parches en la aplicación ({r.ChangedAppBytes} bytes distintos)." +
            (r.Logo == LogoAction.Ninguna ? "" : $"  Logo: {(r.Logo == LogoAction.Girar ? "girado 180°" : "propio")}, {r.LogoLength:N0} bytes.") +
            "  Direcciones = RAM de la app.";
        if (items.Count > 0) LstChanges.SelectedIndex = 0;
        else RtbHex.Document = new System.Windows.Documents.FlowDocument();
    }

    private void OnChangeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (LstChanges.SelectedItem is not ChangeItem c) return;
        var doc = new System.Windows.Documents.FlowDocument { PageWidth = 1200 };
        var para = new System.Windows.Documents.Paragraph { Margin = new Thickness(0) };
        var red = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00));
        void Add(string text, bool changed = false)
        {
            var run = new System.Windows.Documents.Run(text);
            if (changed)
            {
                run.Foreground = red;
                run.FontWeight = FontWeights.Bold;
            }
            para.Inlines.Add(run);
        }
        Add("Dirección   Original                                          Nuevo\n");
        for (int off = 0; off < c.Original.Length; off += 16)
        {
            int n = Math.Min(16, c.Original.Length - off);
            Add($"0x{c.Address + off:X6}  ");
            for (int k = 0; k < 16; k++)
                Add(k < n ? $"{c.Original[off + k]:X2} " : "   ", k < n && c.Original[off + k] != c.New[off + k]);
            Add(" ");
            for (int k = 0; k < n; k++)
                Add($"{c.New[off + k]:X2} ", c.Original[off + k] != c.New[off + k]);
            Add("\n");
        }
        doc.Blocks.Add(para);
        RtbHex.Document = doc;
    }

    private void Log(string line) => TxtLog.AppendText(line + Environment.NewLine);

    private void SetBusy(bool busy, string text)
    {
        BtnOpen.IsEnabled = !busy;
        BtnGenerate.IsEnabled = !busy;
        CmbProfile.IsEnabled = !busy;
        PanelOptions.IsEnabled = !busy;
        PanelKeyMap.IsEnabled = !busy;
        BtnCancel.IsEnabled = busy && _cts is not null;
        TxtBusy.Text = text;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    // ------------------------------------------------------------------ Informe del análisis

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        if (_reportText is not null)
            Clipboard.SetText(_reportText);
    }

    private void OnSaveReport(object sender, RoutedEventArgs e)
    {
        if (_reportText is null || _reportJson is null)
            return;
        var dlg = new SaveFileDialog
        {
            Title = "Guardar informe",
            Filter = "Informe de texto (*.txt)|*.txt|Informe JSON (*.json)|*.json",
            FileName = Path.GetFileNameWithoutExtension(_lastFile) + "_informe",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true)
            return;

        // Protección: nunca escribir el informe sobre un .BIN (ni sobre el archivo analizado).
        string ext = Path.GetExtension(dlg.FileName);
        if (ext.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFullPath(dlg.FileName), _lastFile, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "No se puede guardar el informe sobre un archivo de firmware.", "FirmReaper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string content = dlg.FilterIndex == 2 ? _reportJson : _reportText;
        File.WriteAllText(dlg.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}

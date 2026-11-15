using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using FirmReaper.Core.Analysis;
using FirmReaper.Core.Catalog;
using FirmReaper.Core.Reporting;
using Microsoft.Win32;

namespace FirmReaper.App;

/// <summary>
/// Ventana principal (etapa 1): abrir un .BIN, identificarlo, mostrar cabecera/CRC/recursos y
/// guardar el informe. NO modifica ningún archivo de firmware.
/// </summary>
public partial class MainWindow : Window
{
    private readonly FirmwareCatalog _catalog;
    private string? _reportText;
    private string? _reportJson;
    private string? _lastFile;

    public MainWindow()
    {
        InitializeComponent();
        _catalog = FirmwareCatalog.LoadEmbedded();
    }

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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            _ = AnalyzeAsync(files[0]);
    }

    private async Task AnalyzeAsync(string path)
    {
        SetBusy(true);
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
            SetBusy(false);
        }
    }

    private void Show(FirmwareAnalysis a, Identification id)
    {
        _lastFile = a.FilePath;
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
    }

    private void SetBusy(bool busy)
    {
        BtnOpen.IsEnabled = !busy;
        TxtBusy.Text = busy ? "Analizando…" : "";
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using FirmReaper.Core.Profiles;
using Microsoft.Win32;

namespace FirmReaper.App;

/// <summary>
/// Editor de la tabla botón → función de un mando (perfiles con "tablaMando"). Muestra los botones conocidos del
/// perfil y los códigos que el usuario añade (botones de otro mando).
/// </summary>
public partial class KeyMapWindow : Window
{
    /// <summary>Fila del editor: un botón del mando y la función elegida (-1 = sin función).</summary>
    public sealed class KeyRow : INotifyPropertyChanged
    {
        private int _functionIndex;

        public required byte Code { get; init; }
        public required string ButtonName { get; init; }
        public string CodeText => $"0x{Code:X2}";

        public int FunctionIndex
        {
            get => _functionIndex;
            set
            {
                if (_functionIndex == value) return;
                _functionIndex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FunctionIndex)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed record FunctionItem(int Index, string Label);

    private readonly Profile _profile;
    private readonly KeyTableSpec _spec;
    private readonly ObservableCollection<KeyRow> _rows = [];

    /// <summary>Resultado al pulsar Aceptar.</summary>
    public List<KeyAssignment>? Result { get; private set; }

    public KeyMapWindow(Profile profile, IReadOnlyList<KeyAssignment> current)
    {
        InitializeComponent();
        _profile = profile;
        _spec = profile.KeyTable ?? throw new ArgumentException("El perfil no tiene tabla del mando.");

        var functions = new List<FunctionItem> { new(-1, "(sin función)") };
        functions.AddRange(_spec.Functions.OrderBy(f => f.Key).Select(f => new FunctionItem(f.Key, $"0x{f.Key:X2}  {f.Value}")));
        ColFunction.ItemsSource = functions;

        TxtNote.Text = _spec.Note ?? "";
        TxtNote.Visibility = string.IsNullOrEmpty(_spec.Note) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (code, name) in _spec.Buttons) AddRow(code, name);
        Grid.ItemsSource = _rows;
        Load(current);
    }

    private KeyRow AddRow(byte code, string name)
    {
        var row = new KeyRow { Code = code, ButtonName = name, FunctionIndex = -1 };
        row.PropertyChanged += (_, _) => UpdateCount();
        _rows.Add(row);
        return row;
    }

    private void Load(IReadOnlyList<KeyAssignment> map)
    {
        // Códigos de la tabla que no son botones conocidos (otro mando): se añaden como filas.
        foreach (var a in map)
            if (_rows.All(r => r.Code != a.Code)) AddRow(a.Code, _spec.ButtonName(a.Code));
        foreach (var r in _rows)
        {
            var a = map.FirstOrDefault(k => k.Code == r.Code);
            r.FunctionIndex = a is null ? -1 : a.Index;
        }
        UpdateCount();
    }

    /// <summary>
    /// Construye la tabla: primero los botones en el orden de la tabla del perfil (así, si no se cambia nada,
    /// sale exactamente la misma tabla), después el resto en el orden del mando.
    /// </summary>
    private List<KeyAssignment> Build()
    {
        var assigned = _rows.Where(r => r.FunctionIndex >= 0).ToDictionary(r => r.Code, r => (byte)r.FunctionIndex);
        var map = new List<KeyAssignment>();
        foreach (var d in _spec.Default)
            if (assigned.Remove(d.Code, out byte idx)) map.Add(new KeyAssignment(d.Code, idx));
        foreach (var r in _rows)
            if (assigned.Remove(r.Code, out byte idx)) map.Add(new KeyAssignment(r.Code, idx));
        return map;
    }

    private void UpdateCount()
    {
        int n = _rows.Count(r => r.FunctionIndex >= 0);
        TxtCount.Text = $"{n} de {_spec.MaxAssignments} teclas";
        TxtCount.Foreground = n > _spec.MaxAssignments ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Black;
    }

    private void OnRestore(object sender, RoutedEventArgs e) => Load(_spec.Default);

    private void OnAddCode(object sender, RoutedEventArgs e)
    {
        var t = TxtNewCode.Text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.Length is < 1 or > 2 || !byte.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out byte code))
        {
            MessageBox.Show(this, "Escribe el código del botón en hexadecimal (00–FF).", "FirmReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var row = _rows.FirstOrDefault(r => r.Code == code) ?? AddRow(code, _spec.ButtonName(code));
        Grid.SelectedItem = row;
        Grid.ScrollIntoView(row);
        TxtNewCode.Clear();
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Importar tabla del mando", Filter = "Tabla del mando (*.json)|*.json", CheckFileExists = true };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > 256 * 1024) throw new InvalidDataException("Archivo demasiado grande.");
            Load(_spec.FromJson(File.ReadAllBytes(dlg.FileName), _profile.Id));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "No se pudo importar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var map = Build();
        var errors = _spec.Validate(map);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), "Tabla no válida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "Exportar tabla del mando",
            Filter = "Tabla del mando (*.json)|*.json",
            FileName = _profile.Id + "_tabla_mando.json",
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        if (!dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "La tabla solo se guarda como archivo .json.", "FirmReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        File.WriteAllText(dlg.FileName, _spec.ToJson(_profile.Id, map), new UTF8Encoding(false));
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var map = Build();
        var errors = _spec.Validate(map);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), "Tabla no válida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = map;
        DialogResult = true;
    }
}

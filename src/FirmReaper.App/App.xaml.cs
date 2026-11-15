using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace FirmReaper.App;

/// <summary>
/// Aplicación WPF de FirmReaper. Si ocurre un error no controlado, NO se cierra en silencio:
/// lo muestra en pantalla y lo guarda en %LOCALAPPDATA%\FirmReaper\errores.log.
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, fatal: false);
            e.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception, fatal: false);
        e.Handled = true; // la ventana sigue abierta
    }

    /// <summary>Ruta del registro de errores (sin permisos de administrador).</summary>
    public static string ErrorLogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirmReaper", "errores.log");

    private static void Report(Exception? ex, bool fatal)
    {
        string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "ERROR FATAL" : "ERROR")}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, text, Encoding.UTF8);
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
            // Si no se puede escribir el registro, al menos se muestra el mensaje.
        }
        MessageBox.Show(
            $"Se produjo un error inesperado{(fatal ? " y el programa se cerrará" : "")}:\n\n{ex?.Message}\n\nDetalle guardado en:\n{ErrorLogPath}",
            "FirmReaper — error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

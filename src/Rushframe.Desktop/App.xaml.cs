using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Rushframe.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        WriteStartupDiagnostic("app.on_startup");

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            WriteStartupDiagnostic("app.window_created");
            window.Show();
            WriteStartupDiagnostic("app.window_show_called");
        }
        catch (Exception ex)
        {
            WriteStartupDiagnostic($"app.startup_failed|{ex}");
            MessageBox.Show(
                $"Rushframe could not start.\n\n{ex.Message}",
                "Rushframe startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteStartupDiagnostic($"app.on_exit|code={e.ApplicationExitCode}");
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupDiagnostic($"app.dispatcher_unhandled|{e.Exception}");
    }

    private static void WriteStartupDiagnostic(string message)
    {
        var diagnosticPath = Environment.GetEnvironmentVariable("RUSHFRAME_STARTUP_LOG");
        if (string.IsNullOrWhiteSpace(diagnosticPath)) return;
        try
        {
            var path = Path.GetFullPath(diagnosticPath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"{DateTime.UtcNow:O}|APP|{message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent startup or shutdown.
        }
    }
}

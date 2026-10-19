using System.IO;
using System.Windows;

namespace TaskbarAiUsage;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            SessionEnding += (_, _) =>
            {
                window.BeginSessionShutdown();
                Shutdown();
            };
            window.Show();
            window.WindowState = WindowState.Minimized;
        }
        catch (Exception exception)
        {
            var message = exception is InvalidDataException ? exception.Message : "Settings could not be loaded. Check appsettings.json and its location.";
            SafeLog.Write("startup-failed", $"{exception.GetType().Name}: {message}");
            MessageBox.Show(message, "Taskbar AI Usage configuration error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

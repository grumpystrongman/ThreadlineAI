using Microsoft.UI.Xaml;

namespace Threadline.Windows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        UnhandledException += (_, args) =>
        {
            LogMessage("WinUI unhandled exception captured.");
            LogException(args.Exception);
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogException(args.ExceptionObject as Exception ?? new InvalidOperationException("Unknown non-Exception failure."));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(args.Exception);
            args.SetObserved();
        };

        try
        {
            InitializeComponent();
            LogMessage("Application initialized.");
        }
        catch (Exception ex)
        {
            LogException(ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            LogMessage("Application launch started.");
            _window = new MainWindow();
            LogMessage("Main window constructed.");
            _window.Activate();
            LogMessage("Main window activated.");
        }
        catch (Exception ex)
        {
            LogException(ex);
            throw;
        }
    }

    private static void LogMessage(string message)
    {
        Directory.CreateDirectory(GetLogDirectory());
        File.AppendAllText(GetLogPath(), $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
    }

    private static void LogException(Exception exception)
    {
        Directory.CreateDirectory(GetLogDirectory());
        File.AppendAllText(GetLogPath(), $"{DateTimeOffset.Now:o} ERROR{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    private static string GetLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreadlineAI", "logs");

    private static string GetLogPath() =>
        Path.Combine(GetLogDirectory(), "Threadline.Windows.log");
}

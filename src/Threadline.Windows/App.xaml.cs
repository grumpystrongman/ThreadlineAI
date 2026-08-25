using Microsoft.UI.Xaml;
using Threadline.Windows.Services;

namespace Threadline.Windows;

public partial class App : Application
{
    private Window? _window;
    private WindowsDeviceAgentPipeHost? _deviceAgentHost;

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

            var mainWindow = new MainWindow();
            _window = mainWindow;
            LogMessage("Main AI window constructed.");

            _window.Activate();
            LogMessage("Main AI window activated.");

            mainWindow.EnsureJarvisFrontAndCenterStartedAfterActivation();
            LogMessage("AIKA / JARVIS front-and-center startup requested after activation.");

            StartInteractiveDeviceAgent();
            _ = StartLocalAiRuntimeAfterWindowIsVisibleAsync(mainWindow);
        }
        catch (Exception ex)
        {
            LogException(ex);
            throw;
        }
    }

    private void StartInteractiveDeviceAgent()
    {
        try
        {
            _deviceAgentHost ??= new WindowsDeviceAgentPipeHost();
            _deviceAgentHost.Start();
            LogMessage($"Windows Device Agent host started on current-user pipe '{WindowsDeviceAgentPipeHost.PipeName}'.");
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private static async Task StartLocalAiRuntimeAfterWindowIsVisibleAsync(MainWindow mainWindow)
    {
        try
        {
            var serviceTask = ThreadlineServiceLauncher.EnsureStartedAsync();
            var jarvisTask = PersonalJarvisRuntimeLauncher.EnsureStartedAsync();

            await Task.WhenAll(serviceTask, jarvisTask);

            var serviceResult = serviceTask.Result;
            var jarvisResult = jarvisTask.Result;
            LogMessage(serviceResult.Message);
            LogMessage(jarvisResult.Message);

            mainWindow.ReportAiRuntimeStartup(
                serviceResult.Success,
                serviceResult.Message,
                jarvisResult.Success,
                jarvisResult.Installed,
                jarvisResult.Message);
        }
        catch (Exception ex)
        {
            // The assistant window is intentionally independent of runtime startup.
            // A provider/runtime can be repaired without making the visible app disappear.
            LogException(ex);
            mainWindow.ReportAiRuntimeStartup(
                threadlineReady: false,
                threadlineMessage: "Local AI startup encountered an unexpected error.",
                jarvisReady: false,
                jarvisInstalled: true,
                jarvisMessage: ex.Message);
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

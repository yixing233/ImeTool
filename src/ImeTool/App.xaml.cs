using System.IO;
using System.Threading;
using System.Windows;

namespace ImeTool;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Diagnostics.DiagnosticsLog.Write($"Startup arguments: {string.Join(' ', e.Args)}");

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Global\ImeTool.SingleInstance", createdNew: out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _controller = new AppController();
        _controller.Start();
        ScheduleSuccessfulUpdateStartupReport(e.Args);

        bool trayPreviewRequested = e.Args.Any(arg => string.Equals(arg, "--tray-menu", StringComparison.OrdinalIgnoreCase));

        if (StartupLaunchPolicy.ShouldShowSettings(e.Args, _controller.SilentStart))
        {
            Dispatcher.BeginInvoke(_controller.ShowSettings);
        }

        if (trayPreviewRequested)
        {
            Diagnostics.DiagnosticsLog.Write("Development tray menu preview requested.");
            Dispatcher.BeginInvoke(_controller.ShowTrayMenu);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ScheduleSuccessfulUpdateStartupReport(IReadOnlyList<string> arguments)
    {
        string? healthPath = StartupLaunchPolicy.GetArgumentValue(arguments, "--update-health-check");
        if (string.IsNullOrWhiteSpace(healthPath))
        {
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            // Require the WPF dispatcher and controller timer to run before acknowledging
            // the new executable. The updater also observes the process after this signal.
            await Task.Delay(TimeSpan.FromSeconds(2));
            try
            {
                File.WriteAllText(healthPath, "ok");
                Diagnostics.DiagnosticsLog.Write("Update startup health check completed.");
            }
            catch (Exception exception)
            {
                Diagnostics.DiagnosticsLog.Write($"Unable to report update startup health: {exception.Message}");
            }
        });
    }
}


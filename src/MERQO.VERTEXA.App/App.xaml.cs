using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.App.ViewModels;
using Merqo.Vertexa.App.Views;

namespace Merqo.Vertexa.App;

/// <summary>
/// Application entry point: installs the error boundary, composes the object graph,
/// applies the persisted theme and shows the shell. A failure in any sub-service is
/// logged and degraded, never silently swallowed and never fatal to the UI.
/// </summary>
public partial class App : Application
{
    private AppHost? _host;
    private ShellWindow? _shell;

    public static new App Current => (App)Application.Current;

    public AppHost Host => _host ?? throw new InvalidOperationException("Application host is not initialised.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Headless self test: used by CI to prove the shipped binary can compose, scan and
        // export reports on real Windows without a display session.
        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var code = Services.SelfTest.Run();
            Shutdown(code);
            return;
        }

        if (Environment.OSVersion.Version.Build < 17763)
        {
            MessageBox.Show(
                "MERQO VERTEXA requires Windows 10 version 1809 (build 17763) or newer, or Windows 11.\n\n" +
                $"This machine reports build {Environment.OSVersion.Version.Build}. Several diagnostic interfaces " +
                "the product depends on do not exist on older builds, so running it here would produce incomplete results.",
                "MERQO VERTEXA — unsupported Windows version", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(2);
            return;
        }

        try
        {
            _host = new AppHost();
            ThemeManager.Apply(_host.Settings.Theme);

            var shellViewModel = new ShellViewModel(_host);
            _shell = new ShellWindow { DataContext = shellViewModel };
            MainWindow = _shell;
            _shell.Show();
            _ = shellViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            ReportUnhandled("startup", ex, fatal: true);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Shutdown();
        }
        catch
        {
            // Shutdown must never throw on the way out.
        }
        base.OnExit(e);
    }

    /// <summary>Central error surface for commands, tasks and dispatch exceptions.</summary>
    public void ReportUnhandled(string area, Exception exception, bool fatal = false)
    {
        try
        {
            _host?.Logger.Error(area, "unhandled exception: " + exception.Message, exception);
        }
        catch
        {
            // Logging failed too; still show something to the user below.
        }

        if (fatal)
        {
            MessageBox.Show(
                "MERQO VERTEXA could not continue.\n\n" + exception.GetType().Name + ": " + exception.Message +
                "\n\nA log entry was written to:\n" + (_host?.LogDirectory ?? "%LOCALAPPDATA%\\MERQO\\VERTEXA\\logs"),
                "MERQO VERTEXA — fatal error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            "That action could not be completed.\n\n" + exception.GetType().Name + ": " + exception.Message +
            "\n\nThe application is still running and other diagnostics are unaffected.",
            "MERQO VERTEXA — action failed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnhandled("dispatcher", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) ReportUnhandled("appdomain", exception, fatal: true);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportUnhandled("task", e.Exception);
        e.SetObserved();
    }
}

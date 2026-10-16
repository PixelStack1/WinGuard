using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinGuard.Services;

namespace WinGuard;

public partial class App : Application
{
    private int _errorDialogOpen;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        string ver = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        AppLog.Info($"WinGuard {ver} starting (args: {string.Join(' ', e.Args)}) on {Environment.OSVersion}");

        if (e.Args.Any(a => a.Equals("--reapply", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await AutoReapplyService.ReapplyAsync(); }
            catch (Exception ex) { AppLog.Error("Headless reapply failed", ex); }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        try { new Views.MainWindow().Show(); }
        catch (Exception ex)
        {
            AppLog.Error("Main window failed to open", ex);
            ShowFriendly(ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"WinGuard exiting (code {e.ApplicationExitCode}).");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        AppLog.Error("Unhandled UI exception", args.Exception);
        args.Handled = true; // keep the app alive
        ShowFriendly(args.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        // Background-thread crash: the runtime will terminate after this when IsTerminating; log first.
        AppLog.Error($"Unhandled background exception (terminating={args.IsTerminating})", args.ExceptionObject as Exception);
        if (args.IsTerminating)
        {
            try
            {
                MessageBox.Show(((args.ExceptionObject as Exception)?.Message ?? "?") + "\n\n" + Localization.Loc.F("Log.LogFile", AppLog.FilePath),
                    Localization.Loc.T("UnexpectedError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }

    private void ShowFriendly(Exception ex)
    {
        // Avoid stacking dialogs if errors repeat in a loop.
        if (Interlocked.Exchange(ref _errorDialogOpen, 1) == 1) return;
        try
        {
            MessageBox.Show(Localization.Loc.F("Msg.Crash", ex.Message, AppLog.FilePath),
                Localization.Loc.T("UnexpectedError"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
        finally { Interlocked.Exchange(ref _errorDialogOpen, 0); }
    }
}

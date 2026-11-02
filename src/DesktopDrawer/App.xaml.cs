using System.Windows;
using System.Windows.Threading;
using DesktopDrawer.Helpers;

namespace DesktopDrawer;

public partial class App : Application
{
    private const string MutexName = @"Local\DesktopDrawer.SingleInstance";
    private const string ActivateEventName = @"Local\DesktopDrawer.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private bool _shuttingDown;
    private Thread? _activationThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                _activateEvent.Set();
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not initialize single-instance guard");
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLog.Write(exception, "Unhandled exception");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        _activationThread = new Thread(ActivationLoop)
        {
            IsBackground = true,
            Name = "DesktopDrawer.Activation"
        };
        _activationThread.Start();

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void ActivationLoop()
    {
        if (_activateEvent is null)
        {
            return;
        }

        while (!_shuttingDown)
        {
            try
            {
                if (!_activateEvent.WaitOne(1000))
                {
                    continue;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_shuttingDown)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.ShowFromTray();
                }
            });
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception, "Dispatcher exception");
        MessageBox.Show(
            $"Desktop Drawer hit an unexpected error and needs to close.\n\n{e.Exception.Message}\n\nDetails were written to:\n{AppLog.LogPath}",
            "Desktop Drawer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        try
        {
            _activateEvent?.Set();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _activateEvent?.Dispose();
        _activateEvent = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // The mutex may not be owned in second-instance scenarios.
        }

        _mutex?.Dispose();
        _mutex = null;

        base.OnExit(e);
    }
}

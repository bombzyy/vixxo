using System.IO;
using System.Threading;
using System.Windows;

namespace Vixxo.App;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Global exception logging
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = false;
        };

        const string mutexName = "Local\\Vixxo_Microphone_SingleInstance_Mutex";
        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed or was killed without releasing the mutex
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show("Vixxo is already running. Please check your system tray.", "Vixxo", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vixxo");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

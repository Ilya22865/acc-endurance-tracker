using Avalonia;
using Avalonia.Threading;
using AccEndurance.Client.Diagnostics;
using Velopack;

namespace AccEndurance.Client;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must process install/update lifecycle arguments before Avalonia starts.
        VelopackApp.Build().Run();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DebugLog.Write("CRASH: " + (e.ExceptionObject as Exception)?.ToString());
        TaskScheduler.UnobservedTaskException += (_, e) =>
            DebugLog.Write("TASK: " + e.Exception);
        Dispatcher.UIThread.UnhandledException += (_, e) =>
            DebugLog.Write("UI CRASH: " + e.Exception);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
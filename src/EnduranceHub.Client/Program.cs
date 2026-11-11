using Avalonia;
using Avalonia.Threading;
using EnduranceHub.Client.Diagnostics;
using EnduranceHub.Shared;
using Velopack;

namespace EnduranceHub.Client;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must process install/update lifecycle arguments before Avalonia starts.
        VelopackApp.Build().Run();

        // Один раз переносим настройки/контуры из старой папки AccEndurance.
        AppPaths.MigrateLegacyData();

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
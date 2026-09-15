using Avalonia;
using AccEndurance.Client.Diagnostics;

namespace AccEndurance.Client;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DebugLog.Write("CRASH: " + (e.ExceptionObject as Exception)?.ToString());
        TaskScheduler.UnobservedTaskException += (_, e) =>
            DebugLog.Write("TASK: " + e.Exception);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
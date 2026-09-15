using AccEndurance.Client.ViewModels;
using AccEndurance.Client.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

namespace AccEndurance.Client;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel($"http://{GetServerAddress()}") };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetServerAddress()
    {
        var env = System.Environment.GetEnvironmentVariable("ACC_SERVER");
        return !string.IsNullOrWhiteSpace(env) ? env : "localhost:5261";
    }
}
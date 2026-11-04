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
            var address = GetServerAddress().Trim().TrimEnd('/');
            // в server.url обычно уже лежит "http://host:port"; раньше всегда добавлялось ещё одно "http://"
            var baseUrl = address.Contains("://") ? address : $"http://{address}";
            var mainWindow = new MainWindow { DataContext = new MainViewModel(baseUrl) };
            mainWindow.Opened += async (_, _) =>
                await Services.UpdateService.CheckAndApplyAsync();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetUserDataDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(userProfile, "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Combine(root, "AccEndurance");
    }

    private static string GetServerAddress()
    {
        // приоритет: пользовательский server.url (с миграцией старого файла рядом с exe),
        // затем переменная окружения ACC_SERVER, иначе localhost
        try
        {
            var userDataDirectory = GetUserDataDirectory();
            Directory.CreateDirectory(userDataDirectory);

            var userConfig = Path.Combine(userDataDirectory, "server.url");
            var legacyConfig = Path.Combine(AppContext.BaseDirectory, "server.url");

            // Migrate the old beside-the-executable config before Velopack replaces app files.
            if (!File.Exists(userConfig) && File.Exists(legacyConfig))
                File.Copy(legacyConfig, userConfig);

            if (File.Exists(userConfig))
            {
                var s = (File.ReadAllText(userConfig) ?? "").Trim();
                if (s.Length > 0) return s;
            }
        }
        catch { /* ignore */ }

        var env = System.Environment.GetEnvironmentVariable("ACC_SERVER");
        return !string.IsNullOrWhiteSpace(env) ? env : "localhost:5261";
    }
}
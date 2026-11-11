using System.Text.Json;
using EnduranceHub.Shared;

namespace EnduranceHub.Client.Services;

public sealed class SettingsService
{
    private readonly string _settingsFilePath;

    public SettingsService()
    {
        var appDataPath = AppPaths.LocalDataDirectory;

        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
}

public sealed class AppSettings
{
    public int OurCarNumber { get; set; } = 0;
    /// <summary>Поменять лево/право на радаре местами (зависит от системы координат ACC).</summary>
    public bool RadarMirror { get; set; } = true;
    public int RadarRangeIndex { get; set; } = 1; // 0 = 20 м, 1 = 40 м, 2 = 60 м
    public bool AgentUseFakeData { get; set; } = false;
    /// <summary>Пользователь уже прошёл (или пропустил) обучение при первом запуске.</summary>
    public bool HasCompletedTour { get; set; } = false;
}

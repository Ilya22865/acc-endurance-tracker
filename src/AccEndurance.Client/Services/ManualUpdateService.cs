using Velopack;
using Velopack.Sources;
using Microsoft.Extensions.Logging;

namespace AccEndurance.Client.Services;

/// <summary>
/// Расширенный сервис обновлений с UI поддержкой
/// </summary>
public sealed class ManualUpdateService
{
    private readonly ILogger _logger;
    private const string DefaultRepository = "https://github.com/Ilya22865/acc-endurance-tracker";

    public event Action<string>? OnStatusChanged;
    public event Action<int>? OnProgressChanged;

    public bool IsVelopackInstalled { get; private set; }

    public ManualUpdateService(ILogger logger)
    {
        _logger = logger;
        try
        {
            var manager = new UpdateManager(new GithubSource(DefaultRepository, null, false));
            IsVelopackInstalled = manager.IsInstalled;
        }
        catch
        {
            IsVelopackInstalled = false;
        }
    }

    public string CurrentVersion
    {
        get
        {
            var version = typeof(ManualUpdateService).Assembly.GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.1.0";
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            OnStatusChanged?.Invoke("🔍 Проверка обновлений на GitHub...");
            _logger.LogInformation("Checking for updates");

            if (!IsVelopackInstalled)
            {
                OnStatusChanged?.Invoke("⚠️ Автообновления доступны только для установленной версии");
                return new UpdateCheckResult
                {
                    CurrentVersion = CurrentVersion,
                    IsUpdateAvailable = false,
                    Message = "Запустите приложение из установщика для автообновлений"
                };
            }

            var manager = new UpdateManager(new GithubSource(DefaultRepository, null, false));
            var updateInfo = await manager.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                OnStatusChanged?.Invoke($"✅ У вас последняя версия ({CurrentVersion})");
                return new UpdateCheckResult
                {
                    CurrentVersion = CurrentVersion,
                    LatestVersion = CurrentVersion,
                    IsUpdateAvailable = false,
                    Message = "Установлена последняя версия"
                };
            }

            OnStatusChanged?.Invoke($"✅ Найдено обновление: {updateInfo.TargetFullRelease.Version}");
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = $"v{updateInfo.TargetFullRelease.Version}",
                IsUpdateAvailable = true,
                UpdateInfo = updateInfo,
                Message = $"Доступна версия {updateInfo.TargetFullRelease.Version}"
            };
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"❌ Ошибка проверки: {ex.Message}");
            _logger.LogError(ex, "Failed to check for updates");
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                IsUpdateAvailable = false,
                Message = $"Ошибка: {ex.Message}"
            };
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(object? updateInfo)
    {
        if (updateInfo is not Velopack.UpdateInfo veloInfo)
        {
            OnStatusChanged?.Invoke("❌ Нет информации об обновлении");
            return false;
        }

        try
        {
            OnStatusChanged?.Invoke("⬇️ Скачивание обновления...");
            _logger.LogInformation("Downloading update");

            var manager = new UpdateManager(new GithubSource(DefaultRepository, null, false));

            // Скачивание с прогрессом
            await manager.DownloadUpdatesAsync(veloInfo, progress =>
            {
                var percentage = (int)progress;
                OnProgressChanged?.Invoke(percentage);
                OnStatusChanged?.Invoke($"⬇️ Скачивание: {percentage}%");
            });

            OnStatusChanged?.Invoke("✅ Обновление скачано. Применение...");
            _logger.LogInformation("Update downloaded, applying");

            // Применить и перезапустить
            manager.ApplyUpdatesAndRestart(veloInfo);

            return true;
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"❌ Ошибка установки: {ex.Message}");
            _logger.LogError(ex, "Failed to download/apply update");
            return false;
        }
    }
}

public sealed class UpdateCheckResult
{
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool IsUpdateAvailable { get; set; }
    public string Message { get; set; } = "";
    public object? UpdateInfo { get; set; }
}

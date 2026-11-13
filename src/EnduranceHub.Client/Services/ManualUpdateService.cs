using System.Reflection;
using System.Text.Json;
using EnduranceHub.Client.Diagnostics;
using Velopack;
using Velopack.Sources;
using Microsoft.Extensions.Logging;

namespace EnduranceHub.Client.Services;

/// <summary>
/// Единая точка работы с обновлениями: проверка, скачивание и установка.
/// Все операции сериализуются одним семафором, чтобы фоновая проверка при старте
/// и ручная установка не конкурировали за блокировку Velopack.
/// </summary>
public sealed class ManualUpdateService
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _repository;
    private const string ConfigFileName = "update-source.json";
    private const string DefaultRepository = "https://github.com/Ilya22865/acc-endurance-tracker";

    public event Action<string>? OnStatusChanged;
    public event Action<int>? OnProgressChanged;

    public bool IsVelopackInstalled { get; private set; }

    /// <summary>Идёт ли сейчас проверка, скачивание или установка.</summary>
    public bool IsBusy => _gate.CurrentCount == 0;

    public ManualUpdateService(ILogger logger)
    {
        _logger = logger;
        _repository = ReadRepositoryUrl();
        try
        {
            var manager = new UpdateManager(new GithubSource(_repository, null, false));
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
            var assembly = typeof(ManualUpdateService).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // InformationalVersion может содержать build-метаданные: "1.0.0-beta+<sha>".
                var version = informational.Split('+')[0].Trim();
                if (version.Length > 0)
                {
                    return $"v{version}";
                }
            }

            var fallback = assembly.GetName().Version;
            return fallback != null ? $"v{fallback.Major}.{fallback.Minor}.{fallback.Build}" : "v0.1.0";
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        if (!await TryEnterAsync(TimeSpan.FromSeconds(3)))
        {
            return BusyResult();
        }

        try
        {
            OnStatusChanged?.Invoke("Проверка обновлений на GitHub...");
            _logger.LogInformation("Checking for updates");
            Log("Checking GitHub Releases for updates...");

            if (!IsVelopackInstalled)
            {
                Log("Skipped: this is not a Velopack-installed build.");
                OnStatusChanged?.Invoke("Автообновления доступны только для установленной версии");
                return new UpdateCheckResult
                {
                    CurrentVersion = CurrentVersion,
                    IsUpdateAvailable = false,
                    Message = "Запустите приложение из установщика для автообновлений"
                };
            }

            var manager = new UpdateManager(new GithubSource(_repository, null, false));
            var updateInfo = await manager.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                Log("Application is up to date.");
                OnStatusChanged?.Invoke($"У вас последняя версия ({CurrentVersion})");
                return new UpdateCheckResult
                {
                    CurrentVersion = CurrentVersion,
                    LatestVersion = CurrentVersion,
                    IsUpdateAvailable = false,
                    Message = "Установлена последняя версия"
                };
            }

            Log($"Update found: {updateInfo.TargetFullRelease.Version}");
            OnStatusChanged?.Invoke($"Найдено обновление: {updateInfo.TargetFullRelease.Version}");
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
            Log("UPDATE CHECK ERROR: " + ex.Message);
            OnStatusChanged?.Invoke($"Ошибка проверки: {ex.Message}");
            _logger.LogError(ex, "Failed to check for updates");
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                IsUpdateAvailable = false,
                Message = $"Ошибка: {ex.Message}"
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(object? updateInfo)
    {
        if (updateInfo is not Velopack.UpdateInfo veloInfo)
        {
            OnStatusChanged?.Invoke("Нет информации об обновлении");
            return false;
        }

        // Даём возможной фоновой операции закончить, но не ждём бесконечно.
        if (!await TryEnterAsync(TimeSpan.FromSeconds(15)))
        {
            Log("Install skipped: another update operation is already running.");
            OnStatusChanged?.Invoke("Проверка или установка обновления уже выполняется. Дождитесь завершения.");
            return false;
        }

        try
        {
            OnStatusChanged?.Invoke("Скачивание обновления...");
            _logger.LogInformation("Downloading update");
            Log("Downloading update...");

            var manager = new UpdateManager(new GithubSource(_repository, null, false));

            // Скачивание с прогрессом
            await manager.DownloadUpdatesAsync(veloInfo, progress =>
            {
                var percentage = (int)progress;
                OnProgressChanged?.Invoke(percentage);
                OnStatusChanged?.Invoke($"Скачивание: {percentage}%");
            });

            OnStatusChanged?.Invoke("Обновление скачано. Применение...");
            _logger.LogInformation("Update downloaded, applying");
            Log("Update downloaded. Applying and restarting...");

            // Применить и перезапустить
            manager.ApplyUpdatesAndRestart(veloInfo);

            return true;
        }
        catch (Exception ex) when (IsAlreadyUpdating(ex))
        {
            // Velopack уже занят другой операцией — это не ошибка пользователя.
            Log("Install skipped: Velopack lock is held by another operation.");
            OnStatusChanged?.Invoke("Проверка или установка обновления уже выполняется. Дождитесь завершения.");
            _logger.LogWarning(ex, "Another update operation is already running");
            return false;
        }
        catch (Exception ex)
        {
            Log("UPDATE ERROR: " + ex);
            OnStatusChanged?.Invoke($"Ошибка установки: {ex.Message}");
            _logger.LogError(ex, "Failed to download/apply update");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Log(string message) => DebugLog.Write("[update] " + message);

    private async Task<bool> TryEnterAsync(TimeSpan timeout)
    {
        try
        {
            return await _gate.WaitAsync(timeout);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private UpdateCheckResult BusyResult()
    {
        Log("Check skipped: another update operation is already running.");
        OnStatusChanged?.Invoke("Проверка или установка обновления уже выполняется. Дождитесь завершения.");
        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = CurrentVersion,
            IsUpdateAvailable = false,
            Busy = true,
            Message = "Проверка или установка обновления уже выполняется"
        };
    }

    private static bool IsAlreadyUpdating(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? "";
            if (message.Contains("exclusive lock", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("another operation", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadRepositoryUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("GitHubRepository", out var value))
                {
                    var repository = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(repository))
                        return repository.TrimEnd('/');
                }
            }
        }
        catch
        {
            // Некорректный конфиг не должен ломать обновления — используем репозиторий по умолчанию.
        }

        return DefaultRepository;
    }
}

public sealed class UpdateCheckResult
{
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool IsUpdateAvailable { get; set; }
    /// <summary>Проверка не выполнялась: другая операция обновления уже идёт.</summary>
    public bool Busy { get; set; }
    public string Message { get; set; } = "";
    public object? UpdateInfo { get; set; }
}

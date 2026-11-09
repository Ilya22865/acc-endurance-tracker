using System.Diagnostics;

namespace AccEndurance.Server.Services;

/// <summary>
/// Останавливает сервер, если клиент, который его запустил, завершился аварийно.
/// Windows не убивает дочерние процессы вместе с родителем, поэтому клиент передаёт
/// свой PID через ACC_PARENT_PID, а сервер раз в пару секунд проверяет, жив ли он.
/// </summary>
public sealed class ParentProcessWatchdog : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger<ParentProcessWatchdog> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly int _parentPid;

    public ParentProcessWatchdog(
        ILogger<ParentProcessWatchdog> logger,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration)
    {
        _logger = logger;
        _lifetime = lifetime;

        var raw = configuration["ParentPid"]
                  ?? Environment.GetEnvironmentVariable("ACC_PARENT_PID");
        if (int.TryParse(raw, out var pid) && pid > 0 && pid != Environment.ProcessId)
            _parentPid = pid;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_parentPid <= 0)
            return;

        _logger.LogInformation("Parent watchdog active: parent PID {ParentPid}", _parentPid);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsParentAlive())
            {
                _logger.LogWarning(
                    "Parent process {ParentPid} is gone; stopping server", _parentPid);
                _lifetime.StopApplication();
                return;
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private bool IsParentAlive()
    {
        try
        {
            using var parent = Process.GetProcessById(_parentPid);
            return !parent.HasExited;
        }
        catch (ArgumentException)
        {
            // Процесса с таким PID больше нет.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Не смогли проверить — не убиваем сервер из-за временной ошибки.
            _logger.LogDebug(ex, "Parent watchdog check failed");
            return true;
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AccEndurance.Client.Services;

/// <summary>
/// Встроенный агент для записи телеметрии педалей/руля
/// </summary>
public sealed class EmbeddedAgentService : IDisposable
{
    private readonly ILogger _logger;
    private Process? _agentProcess;
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly int _maxLogLines = 500;
    private bool _isRunning;

    public event Action<string>? OnLogLine;
    public event Action<bool>? OnStateChanged;

    public bool IsRunning => _isRunning;

    public EmbeddedAgentService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartAsync(string agentExePath, string serverUrl, int driverIndex, string? workingDirectory = null)
    {
        if (_isRunning)
        {
            AddLog("Агент уже запущен");
            return false;
        }

        try
        {
            if (!File.Exists(agentExePath))
            {
                AddLog($"❌ Файл агента не найден: {agentExePath}");
                return false;
            }

            AddLog($"🚀 Запуск Agent: DriverIndex={driverIndex}, Server={serverUrl}");

            var args = $"--server {serverUrl} --driver-index {driverIndex}";

            var startInfo = new ProcessStartInfo
            {
                FileName = agentExePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true, // Для автоматического выбора пилота
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(agentExePath) ?? Environment.CurrentDirectory
            };

            _agentProcess = new Process { StartInfo = startInfo };

            _agentProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AddLog(e.Data);
            };

            _agentProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AddLog($"⚠️ {e.Data}");
            };

            _agentProcess.Exited += (sender, e) =>
            {
                _isRunning = false;
                AddLog("🛑 Agent остановлен");
                OnStateChanged?.Invoke(false);
            };

            _agentProcess.EnableRaisingEvents = true;

            if (!_agentProcess.Start())
            {
                AddLog("❌ Не удалось запустить процесс агента");
                return false;
            }

            _agentProcess.BeginOutputReadLine();
            _agentProcess.BeginErrorReadLine();

            _isRunning = true;
            AddLog("✅ Agent запущен");
            OnStateChanged?.Invoke(true);

            await Task.Delay(500);

            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка запуска агента: {ex.Message}");
            _logger.LogError(ex, "Failed to start embedded agent");
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (_agentProcess == null || !_isRunning)
        {
            AddLog("Agent не запущен");
            return;
        }

        try
        {
            AddLog("⏹️ Остановка Agent...");

            if (!_agentProcess.HasExited)
            {
                _agentProcess.Kill(entireProcessTree: true);
                await _agentProcess.WaitForExitAsync();
            }

            _isRunning = false;
            AddLog("🛑 Agent остановлен");
            OnStateChanged?.Invoke(false);
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Ошибка при остановке: {ex.Message}");
            _logger.LogError(ex, "Error stopping embedded agent");
        }
        finally
        {
            _agentProcess?.Dispose();
            _agentProcess = null;
        }
    }

    public IReadOnlyList<string> GetRecentLogs()
    {
        return _logLines.ToArray();
    }

    private void AddLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logLines.Enqueue(timestamped);

        while (_logLines.Count > _maxLogLines)
            _logLines.TryDequeue(out _);

        OnLogLine?.Invoke(timestamped);
    }

    public void Dispose()
    {
        if (_isRunning)
        {
            StopAsync().Wait();
        }
    }
}

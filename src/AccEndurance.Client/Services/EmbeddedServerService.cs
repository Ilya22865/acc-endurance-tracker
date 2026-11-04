using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AccEndurance.Client.Services;

/// <summary>
/// Встроенный сервер для запуска AccEndurance.Server прямо из клиента
/// </summary>
public sealed class EmbeddedServerService : IDisposable
{
    private readonly ILogger _logger;
    private Process? _serverProcess;
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly int _maxLogLines = 500;
    private bool _isRunning;

    public event Action<string>? OnLogLine;
    public event Action<bool>? OnStateChanged;

    public bool IsRunning => _isRunning;

    public EmbeddedServerService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartAsync(string serverExePath, string? workingDirectory = null)
    {
        if (_isRunning)
        {
            AddLog("Сервер уже запущен");
            return false;
        }

        try
        {
            if (!File.Exists(serverExePath))
            {
                AddLog($"❌ Файл сервера не найден: {serverExePath}");
                return false;
            }

            AddLog($"🚀 Запуск сервера: {serverExePath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = serverExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(serverExePath) ?? Environment.CurrentDirectory
            };

            _serverProcess = new Process { StartInfo = startInfo };

            _serverProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AddLog(e.Data);
            };

            _serverProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AddLog($"⚠️ {e.Data}");
            };

            _serverProcess.Exited += (sender, e) =>
            {
                _isRunning = false;
                AddLog("🛑 Сервер остановлен");
                OnStateChanged?.Invoke(false);
            };

            _serverProcess.EnableRaisingEvents = true;

            if (!_serverProcess.Start())
            {
                AddLog("❌ Не удалось запустить процесс сервера");
                return false;
            }

            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            _isRunning = true;
            AddLog("✅ Сервер запущен");
            OnStateChanged?.Invoke(true);

            // Даём серверу время на старт
            await Task.Delay(1000);

            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка запуска сервера: {ex.Message}");
            _logger.LogError(ex, "Failed to start embedded server");
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (_serverProcess == null || !_isRunning)
        {
            AddLog("Сервер не запущен");
            return;
        }

        try
        {
            AddLog("⏹️ Остановка сервера...");

            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                await _serverProcess.WaitForExitAsync();
            }

            _isRunning = false;
            AddLog("🛑 Сервер остановлен");
            OnStateChanged?.Invoke(false);
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Ошибка при остановке: {ex.Message}");
            _logger.LogError(ex, "Error stopping embedded server");
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
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

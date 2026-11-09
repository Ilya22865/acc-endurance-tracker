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

            // После аварийного закрытия клиента сервер может остаться работать
            // отдельным процессом и держать порт 5261. Останавливаем такие процессы,
            // иначе новый сервер упадёт с "address already in use".
            await StopStaleServerProcessesAsync();

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

            // Сервер сам завершится, если клиент упадёт (watchdog по PID родителя).
            startInfo.Environment["ACC_PARENT_PID"] = Environment.ProcessId.ToString();

            _serverProcess = new Process { StartInfo = startInfo };
            var process = _serverProcess;

            ChildProcessLog.Write("server-console.log",
                $"=== Server start: {serverExePath} (client PID {Environment.ProcessId}) ===");

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ChildProcessLog.Write("server-console.log", e.Data);
                AddLog(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ChildProcessLog.Write("server-console.log", "[stderr] " + e.Data);
                AddLog($"⚠️ {e.Data}");
            };

            process.Exited += (sender, e) =>
            {
                var exitCode = TryGetExitCode(process);
                _isRunning = false;
                ChildProcessLog.Write("server-console.log", $"=== Server exited with code {exitCode} ===");
                AddLog($"🛑 Сервер остановлен (код {exitCode})");
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

            // Даём серверу время на старт и проверяем, что он не упал сразу.
            // Типичная причина мгновенного выхода — занятый порт 5261
            // (например, сервер запущен из другой папки или остался от прошлого запуска).
            await Task.Delay(1500);

            if (_serverProcess != null && _serverProcess.HasExited)
            {
                _isRunning = false;
                AddLog("❌ Сервер завершился сразу после запуска.");
                AddLog("   Проверьте, не занят ли порт 5261 другой программой или старым экземпляром сервера.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка запуска сервера: {ex.Message}");
            ChildProcessLog.Write("server-console.log", "=== Server start failed: " + ex + " ===");
            _logger.LogError(ex, "Failed to start embedded server");
            return false;
        }
    }

    public async Task StopAsync(bool stopStaleProcesses = false)
    {
        if (stopStaleProcesses)
            await StopStaleServerProcessesAsync();

        if (_serverProcess == null || _serverProcess.HasExited)
        {
            _isRunning = false;
            AddLog("Сервер не запущен");
            OnStateChanged?.Invoke(false);
            return;
        }

        try
        {
            AddLog("⏹️ Остановка сервера...");

            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _serverProcess.WaitForExitAsync(timeout.Token);
            }

            _isRunning = false;
            AddLog("🛑 Сервер остановлен");
            OnStateChanged?.Invoke(false);
        }
        catch (OperationCanceledException)
        {
            AddLog("⚠️ Сервер не завершился за 5 секунд");
            _logger.LogWarning("Timed out waiting for embedded server process to exit");
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

    private async Task StopStaleServerProcessesAsync()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("AccEndurance.Server");
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Не удалось проверить старые процессы сервера: {ex.Message}");
            _logger.LogWarning(ex, "Failed to enumerate stale server processes");
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited || (_serverProcess != null && process.Id == _serverProcess.Id))
                    continue;

                AddLog($"⚠️ Найден старый процесс сервера (PID {process.Id}), останавливаю...");
                process.Kill(entireProcessTree: true);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Не удалось остановить старый сервер (PID {process.Id}): {ex.Message}");
                _logger.LogWarning(ex, "Failed to stop stale server process {Pid}", process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public IReadOnlyList<string> GetRecentLogs()
    {
        return _logLines.ToArray();
    }

    private static string TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode.ToString() : "?";
        }
        catch
        {
            return "?";
        }
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

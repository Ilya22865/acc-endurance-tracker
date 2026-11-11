using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EnduranceHub.Client.Services;

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

    public async Task<bool> StartAsync(string agentExePath, string serverUrl, int driverIndex, string? workingDirectory = null, bool useFakeData = false)
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
                AddLog($"Файл агента не найден: {agentExePath}");
                return false;
            }

            // Старые агенты могли остаться после аварийного закрытия клиента.
            // Они продолжают отправлять телеметрию и дублируют данные.
            await StopStaleAgentProcessesAsync();

            AddLog($"Запуск Agent: DriverIndex={driverIndex}, Server={serverUrl}, FakeData={useFakeData}");

            var args = $"--server {serverUrl} --driver-index {driverIndex} --parent-pid {Environment.ProcessId}";
            if (useFakeData)
                args += " --fake";

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

            // Дублируем PID родителя в окружении: даже если аргумент потеряется,
            // Agent завершится вместе с клиентом.
            startInfo.Environment["ENDURANCEHUB_PARENT_PID"] = Environment.ProcessId.ToString();
            startInfo.Environment["ACC_PARENT_PID"] = Environment.ProcessId.ToString();

            _agentProcess = new Process { StartInfo = startInfo };
            var process = _agentProcess;

            ChildProcessLog.Write("agent.log",
                $"=== Agent start: {agentExePath} {args} (client PID {Environment.ProcessId}) ===");

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ChildProcessLog.Write("agent.log", e.Data);
                AddLog(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ChildProcessLog.Write("agent.log", "[stderr] " + e.Data);
                AddLog($"{e.Data}");
            };

            process.Exited += (sender, e) =>
            {
                var exitCode = TryGetExitCode(process);
                _isRunning = false;
                ChildProcessLog.Write("agent.log", $"=== Agent exited with code {exitCode} ===");
                AddLog($"Agent остановлен (код {exitCode})");
                OnStateChanged?.Invoke(false);
            };

            _agentProcess.EnableRaisingEvents = true;

            if (!_agentProcess.Start())
            {
                AddLog("Не удалось запустить процесс агента");
                return false;
            }

            _agentProcess.BeginOutputReadLine();
            _agentProcess.BeginErrorReadLine();

            _isRunning = true;
            AddLog("Agent запущен");
            OnStateChanged?.Invoke(true);

            await Task.Delay(500);

            return true;
        }
        catch (Exception ex)
        {
            AddLog($"Ошибка запуска агента: {ex.Message}");
            ChildProcessLog.Write("agent.log", "=== Agent start failed: " + ex + " ===");
            _logger.LogError(ex, "Failed to start embedded agent");
            return false;
        }
    }

    public async Task StopAsync(bool stopStaleProcesses = false)
    {
        if (stopStaleProcesses)
            await StopStaleAgentProcessesAsync();

        if (_agentProcess == null || _agentProcess.HasExited)
        {
            _isRunning = false;
            AddLog("Agent не запущен");
            OnStateChanged?.Invoke(false);
            return;
        }

        try
        {
            AddLog("Остановка Agent...");

            if (!_agentProcess.HasExited)
            {
                _agentProcess.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _agentProcess.WaitForExitAsync(timeout.Token);
            }

            _isRunning = false;
            AddLog("Agent остановлен");
            OnStateChanged?.Invoke(false);
        }
        catch (OperationCanceledException)
        {
            AddLog("Agent не завершился за 5 секунд");
            _logger.LogWarning("Timed out waiting for embedded agent process to exit");
        }
        catch (Exception ex)
        {
            AddLog($"Ошибка при остановке: {ex.Message}");
            _logger.LogError(ex, "Error stopping embedded agent");
        }
        finally
        {
            _agentProcess?.Dispose();
            _agentProcess = null;
        }
    }

    private async Task StopStaleAgentProcessesAsync()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("EnduranceHub.Agent");
        }
        catch (Exception ex)
        {
            AddLog($"Не удалось проверить старые процессы Agent: {ex.Message}");
            _logger.LogWarning(ex, "Failed to enumerate stale agent processes");
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited || (_agentProcess != null && process.Id == _agentProcess.Id))
                    continue;

                AddLog($"Найден старый процесс Agent (PID {process.Id}), останавливаю...");
                process.Kill(entireProcessTree: true);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                AddLog($"Не удалось остановить старый Agent (PID {process.Id}): {ex.Message}");
                _logger.LogWarning(ex, "Failed to stop stale agent process {Pid}", process.Id);
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

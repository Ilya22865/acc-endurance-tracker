using System.IO;
using EnduranceHub.Shared;

namespace EnduranceHub.Client.Services;

/// <summary>
/// Постоянные логи дочерних процессов (Server/Agent). Клиентский DebugLog пишется
/// во временную папку и теряется при очистке, а эти файлы лежат рядом с server.log
/// и переживают перезапуск — по ним видно, почему Agent завершился.
/// </summary>
internal static class ChildProcessLog
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();

    public static string LogsDirectory { get; } = Path.Combine(
        AppPaths.LocalDataDirectory,
        "logs");

    public static void Write(string fileName, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogsDirectory);
                var path = Path.Combine(LogsDirectory, fileName);

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(path, path + ".1", overwrite: true);

                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Логи не должны ломать запуск/остановку процессов.
        }
    }
}

using Microsoft.Extensions.Logging;

namespace AccEndurance.Server.Diagnostics;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly int _maxBytes;

    public FileLoggerProvider(string path, int maxBytes = 4 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public ILogger CreateLogger(string category) => new FileLogger(_path, _lock, _maxBytes);

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly string _path;
        private readonly object _lock;
        private readonly int _maxBytes;

        public FileLogger(string path, object sync, int maxBytes)
        {
            _path = path;
            _lock = sync;
            _maxBytes = maxBytes;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {logLevel}] {formatter(state, exception)}{(exception is null ? "" : " | " + exception.Message)}";
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (dir is not null) Directory.CreateDirectory(dir);
                    File.AppendAllText(_path, line + Environment.NewLine);
                    var fi = new FileInfo(_path);
                    if (fi.Length > _maxBytes)
                    {
                        File.WriteAllText(_path, $"[log rotated] {line}{Environment.NewLine}");
                    }
                }
            }
            catch { }
        }
    }
}
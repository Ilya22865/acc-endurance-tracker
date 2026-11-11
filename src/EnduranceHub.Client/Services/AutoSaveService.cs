using System.Text.Json;
using EnduranceHub.Shared;
using EnduranceHub.Shared.Dto;
using EnduranceHub.Client.Diagnostics;

namespace EnduranceHub.Client.Services;

public sealed class AutoSaveService : IDisposable
{
    private readonly string _saveFilePath;
    private readonly Timer _timer;
    private SessionSnapshotDto? _lastSnapshot;
    private readonly object _lock = new();

    public AutoSaveService()
    {
        var appDataPath = AppPaths.LocalDataDirectory;

        Directory.CreateDirectory(appDataPath);
        _saveFilePath = Path.Combine(appDataPath, "autosave.json");

        _timer = new Timer(AutoSaveCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        DebugLog.Write($"AutoSave initialized: {_saveFilePath}");
    }

    public void UpdateSnapshot(SessionSnapshotDto snapshot)
    {
        lock (_lock)
        {
            _lastSnapshot = snapshot;
        }
    }

    private void AutoSaveCallback(object? state)
    {
        SessionSnapshotDto? snapshotToSave;
        lock (_lock)
        {
            snapshotToSave = _lastSnapshot;
        }

        if (snapshotToSave == null) return;

        try
        {
            var json = JsonSerializer.Serialize(snapshotToSave, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_saveFilePath, json);
            DebugLog.Write($"AutoSave: saved at {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AutoSave ERROR: {ex.Message}");
        }
    }

    public SessionSnapshotDto? LoadLastSession()
    {
        if (!File.Exists(_saveFilePath))
        {
            DebugLog.Write("AutoSave: no previous session found");
            return null;
        }

        try
        {
            var json = File.ReadAllText(_saveFilePath);
            var snapshot = JsonSerializer.Deserialize<SessionSnapshotDto>(json);

            if (snapshot != null)
            {
                var fileInfo = new FileInfo(_saveFilePath);
                DebugLog.Write($"AutoSave: restored session from {fileInfo.LastWriteTime:HH:mm:ss dd.MM.yyyy}");
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AutoSave load ERROR: {ex.Message}");
            return null;
        }
    }

    public void ClearSave()
    {
        try
        {
            if (File.Exists(_saveFilePath))
            {
                File.Delete(_saveFilePath);
                DebugLog.Write("AutoSave: cleared");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AutoSave clear ERROR: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

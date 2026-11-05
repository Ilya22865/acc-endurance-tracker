using System.Diagnostics;
using AccEndurance.Shared.Dto;

namespace AccEndurance.Client.Services;

public readonly record struct TelemetryPoint(double T, float Gas, float Brake, float Steer);

/// <summary>
/// Кольцевой буфер «живой» телеметрии. Время T — секунды локального таймера клиента:
/// метки времени сервера пересчитываются относительно момента прихода пачки,
/// поэтому часы сервера и клиента синхронизировать не нужно.
/// Используется только из UI-потока.
/// </summary>
public sealed class TelemetryBuffer
{
    private const double KeepSeconds = 130;
    private const int MaxPoints = 20000;

    private readonly List<TelemetryPoint> _points = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public double Now => _clock.Elapsed.TotalSeconds;
    public IReadOnlyList<TelemetryPoint> Points => _points;
    public double LastSampleAt { get; private set; } = double.NegativeInfinity;

    public void Add(IReadOnlyList<TelemetrySampleDto> samples)
    {
        if (samples.Count == 0) return;

        var now = Now;
        var lastTs = samples[samples.Count - 1].Timestamp;

        foreach (var s in samples)
        {
            var t = now - (lastTs - s.Timestamp).TotalSeconds;
            if (t > now) t = now;
            if (_points.Count > 0 && t < _points[_points.Count - 1].T)
                t = _points[_points.Count - 1].T;
            _points.Add(new TelemetryPoint(t, s.Gas, s.Brake, s.SteerAngle));
        }

        LastSampleAt = now;

        var cutoff = now - KeepSeconds;
        var drop = 0;
        while (drop < _points.Count && _points[drop].T < cutoff) drop++;
        if (_points.Count - drop > MaxPoints) drop = _points.Count - MaxPoints;
        if (drop > 0) _points.RemoveRange(0, drop);
    }

    public void Clear()
    {
        _points.Clear();
        LastSampleAt = double.NegativeInfinity;
    }

    public TelemetryPoint? Latest => _points.Count > 0 ? _points[_points.Count - 1] : null;

    /// <summary>Сколько сэмплов в секунду приходит (по последним 2 секундам).</summary>
    public double Rate()
    {
        var from = Now - 2.0;
        var n = 0;
        for (int i = _points.Count - 1; i >= 0 && _points[i].T >= from; i--) n++;
        return n / 2.0;
    }
}

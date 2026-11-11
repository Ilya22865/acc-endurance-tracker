namespace EnduranceHub.Agent;

public readonly record struct TelemetryReading(
    int PacketId,
    float Gas,
    float Brake,
    float SteerAngle)
{
    public readonly bool IsValid => PacketId != 0;
}

/// <summary>Флаги из страницы Graphics памяти ACC. Status: 0 off, 1 replay, 2 live, 3 pause.</summary>
public readonly record struct FlagReading(
    int Status,
    int PlayerFlag,
    bool Yellow,
    bool Yellow1,
    bool Yellow2,
    bool Yellow3,
    bool White,
    bool Green,
    bool Chequered,
    bool Red);

public interface IAccDataSource : IDisposable
{
    event Action<TelemetryReading>? OnSample;
    void Start();
    bool TryReadSample(out TelemetryReading sample);
    bool TryReadFlags(out FlagReading flags);
}

namespace AccEndurance.Agent;

public readonly record struct TelemetryReading(
    int PacketId,
    float Gas,
    float Brake,
    float SteerAngle)
{
    public readonly bool IsValid => PacketId != 0;
}

public interface IAccDataSource : IDisposable
{
    event Action<TelemetryReading>? OnSample;
    void Start();
    bool TryReadSample(out TelemetryReading sample);
}
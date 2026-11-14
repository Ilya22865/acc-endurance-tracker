namespace EnduranceHub.Agent;

public readonly record struct TelemetryReading(
    int PacketId,
    float Gas,
    float Brake,
    float SteerAngle)
{
    public readonly bool IsValid => PacketId != 0;
}

/// <summary>
/// Снимок шин и топлива из страницы Physics (SPageFilePhysics). Давления и температуры
/// приходят по 4 колесам в порядке ПЛ, ПП, ЗЛ, ЗП.
/// </summary>
public readonly record struct TyreReading(
    float FuelLiters,
    float PressureFl,
    float PressureFr,
    float PressureRl,
    float PressureRr,
    float TempFl,
    float TempFr,
    float TempRl,
    float TempRr)
{
    /// <summary>Правдоподобные значения: ACC отдаёт мусор, пока машина не загрузилась.</summary>
    public readonly bool IsValid =>
        PressureFl is > 5 and < 60 && PressureFr is > 5 and < 60 &&
        PressureRl is > 5 and < 60 && PressureRr is > 5 and < 60 &&
        FuelLiters is >= 0 and < 250 &&
        TempFl is > -50 and < 250 && TempFr is > -50 and < 250 &&
        TempRl is > -50 and < 250 && TempRr is > -50 and < 250;

    public readonly float[] Pressures => new[] { PressureFl, PressureFr, PressureRl, PressureRr };
    public readonly float[] CoreTemperatures => new[] { TempFl, TempFr, TempRl, TempRr };
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
    bool TryReadTyres(out TyreReading tyres);
    bool TryReadFlags(out FlagReading flags);
}

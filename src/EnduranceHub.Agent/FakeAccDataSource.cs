namespace EnduranceHub.Agent;

public sealed class FakeAccDataSource : IAccDataSource
{
    private readonly Random _random = new();
    private readonly bool _simulateDriver;

    public FakeAccDataSource(bool simulateDriver = true)
    {
        _simulateDriver = simulateDriver;
    }

    public event Action<TelemetryReading>? OnSample;

    public void Start()
    {
    }

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private int _packetId;

    // Имитация круга: разгон → торможение → поворот → разгон. Нужна, чтобы проверять графики без ACC.
    public bool TryReadSample(out TelemetryReading sample)
    {
        var t = _clock.Elapsed.TotalSeconds;
        var phase = (t % 12.0) / 12.0; // «круг» 12 секунд

        var gas = 0.0;
        var brake = 0.0;
        var steer = 0.0;

        if (_simulateDriver)
        {
            if (phase < 0.45)                      // прямая
            {
                gas = Math.Min(1.0, 0.2 + phase * 4);
                steer = 0.03 * Math.Sin(t * 3);
            }
            else if (phase < 0.55)                 // торможение
            {
                var k = (phase - 0.45) / 0.10;
                brake = 1.0 - 0.6 * k;
                steer = 0.15 * k;
            }
            else if (phase < 0.75)                 // поворот
            {
                var k = (phase - 0.55) / 0.20;
                gas = 0.15 + 0.7 * k;
                steer = 0.75 * Math.Sin(Math.PI * k);
            }
            else                                   // выход и вторая прямая
            {
                gas = 1.0;
                steer = -0.25 * Math.Sin((phase - 0.75) / 0.25 * Math.PI);
            }
        }

        sample = new TelemetryReading(
            ++_packetId,
            (float)Math.Clamp(gas + (_random.NextDouble() - 0.5) * 0.02, 0, 1),
            (float)Math.Clamp(brake + (_random.NextDouble() - 0.5) * 0.02, 0, 1),
            (float)Math.Round(steer, 4));
        OnSample?.Invoke(sample);
        return true;
    }

    // Имитация флагов: в каждом «круге» (12 с) несколько секунд жёлтого в секторе 2
    public bool TryReadFlags(out FlagReading flags)
    {
        var phase = (_clock.Elapsed.TotalSeconds % 36.0);
        var yellow = phase > 24 && phase < 30;
        flags = new FlagReading(2, 0, yellow, false, yellow, false, false, !yellow, false, false);
        return true;
    }

    public void Dispose()
    {
    }
}
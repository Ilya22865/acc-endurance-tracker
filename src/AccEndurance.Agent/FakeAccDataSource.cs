namespace AccEndurance.Agent;

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

    public bool TryReadSample(out TelemetryReading sample)
    {
        var gas = 0.0f;
        var brake = 0.0f;
        var steer = (float)(_random.NextDouble() * 0.4 - 0.2);

        if (_simulateDriver)
        {
            var mode = _random.NextDouble();
            if (mode < 0.7)
            {
                gas = (float)(0.6 + _random.NextDouble() * 0.4);
                steer = (float)(_random.NextDouble() * 0.3 - 0.15);
            }
            else if (mode < 0.9)
            {
                brake = (float)(_random.NextDouble() * 0.8 + 0.2);
                gas = 0;
                steer = (float)(_random.NextDouble() * 0.2 - 0.1);
            }
        }
        else
        {
            gas = 0;
            brake = 0;
        }

        sample = new TelemetryReading(
            _random.Next(),
            gas,
            brake,
            (float)Math.Round(steer, 4));
        OnSample?.Invoke(sample);
        return true;
    }

    public void Dispose()
    {
    }
}
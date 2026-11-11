using EnduranceHub.Shared.Dto;

namespace EnduranceHub.Client.Services;

public sealed class TelemetryComparisonService
{
    public TelemetryComparison CompareDrivers(
        List<StintDto> stints,
        int driverIndex1,
        int driverIndex2)
    {
        var driver1Stints = stints.Where(s => s.DriverIndex == driverIndex1).ToList();
        var driver2Stints = stints.Where(s => s.DriverIndex == driverIndex2).ToList();

        return new TelemetryComparison
        {
            Driver1Name = driver1Stints.FirstOrDefault()?.Driver ?? "Driver 1",
            Driver2Name = driver2Stints.FirstOrDefault()?.Driver ?? "Driver 2",
            Driver1Stats = CalculateStats(driver1Stints),
            Driver2Stats = CalculateStats(driver2Stints),
        };
    }

    private DriverStats CalculateStats(List<StintDto> stints)
    {
        if (stints.Count == 0)
        {
            return new DriverStats
            {
                TotalStints = 0,
                TotalDuration = TimeSpan.Zero,
                AverageDuration = TimeSpan.Zero,
                TotalLaps = 0,
            };
        }

        var totalLaps = stints.Sum(s => s.LapNumber);
        var durations = stints
            .Where(s => s.EndedAt.HasValue)
            .Select(s => s.EndedAt!.Value - s.StartedAt)
            .ToList();

        var totalDuration = TimeSpan.FromSeconds(durations.Sum(d => d.TotalSeconds));
        var averageDuration = durations.Count > 0
            ? TimeSpan.FromSeconds(durations.Average(d => d.TotalSeconds))
            : TimeSpan.Zero;

        return new DriverStats
        {
            TotalStints = stints.Count,
            TotalDuration = totalDuration,
            AverageDuration = averageDuration,
            TotalLaps = totalLaps,
        };
    }
}

public sealed class TelemetryComparison
{
    public string Driver1Name { get; set; } = "";
    public string Driver2Name { get; set; } = "";
    public DriverStats Driver1Stats { get; set; } = new();
    public DriverStats Driver2Stats { get; set; } = new();
}

public sealed class DriverStats
{
    public int TotalStints { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan AverageDuration { get; set; }
    public int TotalLaps { get; set; }
}

namespace AccEndurance.Domain.Entities;

public class Stint
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int DriverId { get; set; }
    public Driver? Driver { get; set; }

    public int DriverIndex { get; set; }
    public int? CarId { get; set; }
    public Car? Car { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public int LapNumber { get; set; }
    public TimeSpan? Duration => EndedAt.HasValue ? EndedAt.Value - StartedAt : null;

    public string? Notes { get; set; }
    public double? PlannedFuelLiters { get; set; }
    public int? PlannedLaps { get; set; }

    public ICollection<Lap> Laps { get; set; } = new List<Lap>();
    public ICollection<TelemetrySample> TelemetrySamples { get; set; } = new List<TelemetrySample>();
}
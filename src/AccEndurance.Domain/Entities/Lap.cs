namespace AccEndurance.Domain.Entities;

public enum LapType
{
    Regular = 0,
    Outlap,
    Inlap,
}

public class Lap
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int? CarId { get; set; }
    public Car? Car { get; set; }

    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }

    public int? StintId { get; set; }
    public Stint? Stint { get; set; }

    public int LapNumber { get; set; }
    public int LaptimeMs { get; set; }
    public LapType Type { get; set; }
    public bool IsInvalid { get; set; }
    public bool IsValidForBest { get; set; }

    public int? Split1Ms { get; set; }
    public int? Split2Ms { get; set; }
    public int? Split3Ms { get; set; }

    public DateTime CompletedAt { get; set; }
    public double? SplinePositionAtStart { get; set; }
}
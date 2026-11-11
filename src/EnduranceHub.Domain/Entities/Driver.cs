namespace EnduranceHub.Domain.Entities;

public enum DriverCategory : byte
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
    Platinum = 3,
}

public class Driver
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public DriverCategory Category { get; set; }
    public ushort Nationality { get; set; }

    public string? AccProfileName { get; set; }
    public int? AccDriverIndex { get; set; }

    public ICollection<Stint> Stints { get; set; } = new List<Stint>();
    public ICollection<Lap> Laps { get; set; } = new List<Lap>();
    public ICollection<TelemetrySample> TelemetrySamples { get; set; } = new List<TelemetrySample>();
}
namespace AccEndurance.Domain.Entities;

public class Car
{
    public int Id { get; set; }
    public byte CarModelType { get; set; }
    public string CarModelName { get; set; } = "";
    public int RaceNumber { get; set; }
    public string TeamName { get; set; } = "";
    public byte CupCategory { get; set; }
    public ushort Nationality { get; set; }

    public ICollection<Stint> Stints { get; set; } = new List<Stint>();
    public ICollection<Lap> Laps { get; set; } = new List<Lap>();
    public ICollection<Setup> Setups { get; set; } = new List<Setup>();
}
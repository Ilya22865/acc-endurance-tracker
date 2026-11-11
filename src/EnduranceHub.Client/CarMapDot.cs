namespace EnduranceHub.Client;

public sealed class CarMapDot
{
    public double X { get; set; }
    public double Y { get; set; }
    public int RaceNumber { get; set; }
    public string Driver { get; set; } = "";
    public bool IsOurCar { get; set; }
}

namespace AccEndurance.Domain.Entities;

public class TelemetrySample
{
    public long Id { get; set; }
    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }

    public int? StintId { get; set; }
    public Stint? Stint { get; set; }

    public DateTime Timestamp { get; set; }
    public float Gas { get; set; }
    public float Brake { get; set; }
    public float SteerAngle { get; set; }
}
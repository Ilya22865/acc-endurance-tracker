namespace AccEndurance.Domain.Entities;

public class Setup
{
    public int Id { get; set; }
    public int? CarId { get; set; }
    public Car? Car { get; set; }
    public string CarModelType { get; set; } = "";
    public string TrackName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string JsonContent { get; set; } = "";
    public DateTime ModifiedAt { get; set; }
}
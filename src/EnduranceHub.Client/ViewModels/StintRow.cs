using CommunityToolkit.Mvvm.ComponentModel;

namespace EnduranceHub.Client.ViewModels;

public sealed partial class StintRow : ObservableObject
{
    public int StintId { get; set; }
    public string? Driver { get; set; }
    public int DriverIndex { get; set; }
    public int Ordinal { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string StartReal { get; set; } = "";
    public string StartGame { get; set; } = "";
    public string EndReal { get; set; } = "";
    public string EndGame { get; set; } = "";
    public int LapNumber { get; set; }
    public bool IsOpen { get; set; }

    [ObservableProperty] private string _duration = "";
}
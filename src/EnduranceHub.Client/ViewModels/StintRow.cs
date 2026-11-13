using CommunityToolkit.Mvvm.ComponentModel;

namespace EnduranceHub.Client.ViewModels;

public sealed partial class StintRow : ObservableObject
{
    public int StintId { get; set; }
    [ObservableProperty] private string? _driver;
    public int DriverIndex { get; set; }
    public int Ordinal { get; set; }
    [ObservableProperty] private DateTime _startedAtUtc;
    [ObservableProperty] private DateTime? _endedAtUtc;
    [ObservableProperty] private string _startReal = "";
    public string StartGame { get; set; } = "";
    [ObservableProperty] private string _endReal = "";
    public string EndGame { get; set; } = "";
    public int LapNumber { get; set; }
    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private string _duration = "";
}

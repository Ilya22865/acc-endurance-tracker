using CommunityToolkit.Mvvm.ComponentModel;

namespace EnduranceHub.Client.ViewModels;

public sealed partial class StintRow : ObservableObject
{
    public int StintId { get; set; }
    [ObservableProperty] private string? _driver;
    public int DriverIndex { get; set; }
    [ObservableProperty] private int _ordinal;
    [ObservableProperty] private DateTime _startedAtUtc;
    [ObservableProperty] private DateTime? _endedAtUtc;
    [ObservableProperty] private string _startReal = "";
    [ObservableProperty] private string _endReal = "";
    public int LapNumber { get; set; }
    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private string _duration = "";
}

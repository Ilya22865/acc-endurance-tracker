using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccEndurance.Client.ViewModels;

public sealed partial class StandingsRow : ObservableObject
{
    [ObservableProperty] private int _position;
    [ObservableProperty] private int _raceNumber;
    [ObservableProperty] private string _teamName = "";
    [ObservableProperty] private string _driver = "";
    [ObservableProperty] private string _carModel = "";
    [ObservableProperty] private int _laps;
    [ObservableProperty] private string _gap = "";
    [ObservableProperty] private string _lastLap = "";
    [ObservableProperty] private string _bestLap = "";
    [ObservableProperty] private bool _isOurCar;
    [ObservableProperty] private bool _isAlt;

    public IBrush RowBackground { get; set; } = Brushes.Transparent;
    public IBrush RowBorderBrush { get; set; } = Brushes.Transparent;
    public Thickness RowBorder { get; set; } = new(0, 0, 0, 1);
    public IBrush DriverBrush { get; set; } = Brushes.White;

    public int Key => RaceNumber;
}
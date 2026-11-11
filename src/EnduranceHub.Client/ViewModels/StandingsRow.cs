using CommunityToolkit.Mvvm.ComponentModel;

namespace EnduranceHub.Client.ViewModels;

/// <summary>Строка таблицы позиций. Объект живёт, пока машина есть в сессии, и обновляется на месте —
/// поэтому выбор строки и прокрутка не сбрасываются при каждом снимке состояния.</summary>
public sealed partial class StandingsRow : ObservableObject
{
    public int CarIndex { get; init; }

    [ObservableProperty] private int _position;
    [ObservableProperty] private int _raceNumber;
    [ObservableProperty] private string _teamName = "";
    [ObservableProperty] private string _driver = "";
    [ObservableProperty] private string _carModel = "";
    [ObservableProperty] private int _laps;
    [ObservableProperty] private bool _isOurCar;
    [ObservableProperty] private bool _inPit;

    // Время круга. Фиолетовый — лучший круг сессии, зелёный — личный лучший.
    [ObservableProperty] private string _lastLap = "—";
    [ObservableProperty] private string _bestLap = "—";
    [ObservableProperty] private bool _lastIsSessionBest;
    [ObservableProperty] private bool _lastIsPersonalBest;
    [ObservableProperty] private bool _bestIsSessionBest;

    // Отрывы (только гонка)
    [ObservableProperty] private string _gapToLeader = "";
    [ObservableProperty] private string _intervalAhead = "";
    [ObservableProperty] private string _intervalBehind = "—";
    [ObservableProperty] private string _gapToOur = "—";
    [ObservableProperty] private string _gapToOurLabel = "";
    [ObservableProperty] private bool _gapToOurAhead;
    [ObservableProperty] private bool _gapToOurBehind;
    [ObservableProperty] private string _trendText = "";
    [ObservableProperty] private bool _trendGood;
    [ObservableProperty] private bool _trendBad;
}

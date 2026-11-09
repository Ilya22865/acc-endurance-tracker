using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using AccEndurance.Client.Diagnostics;
using AccEndurance.Shared.Dto;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccEndurance.Client.ViewModels;

// «Живая» часть вью-модели: часы сессии, флаги, таблица позиций с отрывами, радар, пилоты команды.
public sealed partial class MainViewModel
{
    private readonly Stopwatch _clockWatch = Stopwatch.StartNew();

    // ------------------------------------------------------------------ шапка
    [ObservableProperty] private string _trackTitle = "ACC Endurance";
    [ObservableProperty] private string _sessionTypeText = "—";
    [ObservableProperty] private bool _isRaceSession;
    [ObservableProperty] private string _ownNumberText = "—";
    [ObservableProperty] private string _ownDriverName = "Наша машина не найдена";
    [ObservableProperty] private string _ownStatusText = "";

    // ------------------------------------------------------------------ часы
    [ObservableProperty] private string _remainingText = "--:--:--";
    [ObservableProperty] private string _elapsedText = "";
    [ObservableProperty] private string _clockNote = "";

    private TimeSpan _remainingAnchor;
    private TimeSpan _elapsedAnchor;
    private double _anchorAtSec;
    private double _clockScale = 1;
    private bool _clockRunning;
    private bool _hasClock;

    private void ApplyClock(RealtimeStateDto rt)
    {
        // RemainingTime считает сервер (уже в реальном времени). Старый сервер присылает 0 — запасная формула.
        var remaining = rt.RemainingTime > TimeSpan.Zero
            ? rt.RemainingTime
            : (rt.SessionEndTime > rt.SessionTime ? rt.SessionEndTime - rt.SessionTime : TimeSpan.Zero);

        _remainingAnchor = remaining;
        _elapsedAnchor = rt.SessionTime;
        _anchorAtSec = _clockWatch.Elapsed.TotalSeconds;
        _clockScale = rt.ClockScale > 0 ? rt.ClockScale : 1;
        _clockRunning = rt.ClockRunning && remaining > TimeSpan.Zero;
        _hasClock = true;

        ClockNote = !rt.ClockRunning ? "часы стоят"
            : Math.Abs(_clockScale - 1) > 0.02 ? $"часы ACC идут ×{_clockScale.ToString("0.##", CultureInfo.CurrentCulture)}"
            : "";
        TickClock();
    }

    /// <summary>Вызывается таймером окна несколько раз в секунду: обратный отсчёт идёт плавно между снимками сервера.</summary>
    public void TickClock()
    {
        if (!_hasClock) return;

        var dt = _clockWatch.Elapsed.TotalSeconds - _anchorAtSec;
        // если снимки перестали приходить (потеря связи) — не рисуем «живое» время
        var live = _clockRunning && dt < 5;
        var remaining = live ? _remainingAnchor - TimeSpan.FromSeconds(dt) : _remainingAnchor;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var elapsed = live ? _elapsedAnchor + TimeSpan.FromSeconds(dt * _clockScale) : _elapsedAnchor;

        RemainingText = FormatClock(remaining);
        ElapsedText = "прошло " + FormatClock(elapsed);
        TimeText = RemainingText;
    }

    // ------------------------------------------------------------------ флаги
    [ObservableProperty] private string _flagKind = "None";
    [ObservableProperty] private string _flagText = "Нет сессии";
    [ObservableProperty] private string _flagDetail = "";
    [ObservableProperty] private bool _isFlagNeutral = true;
    [ObservableProperty] private bool _isFlagGreen;
    [ObservableProperty] private bool _isFlagYellow;
    [ObservableProperty] private bool _isFlagRed;
    [ObservableProperty] private bool _isFlagWhite;
    [ObservableProperty] private bool _isFlagChequered;
    [ObservableProperty] private bool _showFlagBanner;
    [ObservableProperty] private bool _yellowS1;
    [ObservableProperty] private bool _yellowS2;
    [ObservableProperty] private bool _yellowS3;
    [ObservableProperty] private string _playerFlagText = "";
    [ObservableProperty] private bool _hasPlayerFlag;

    private void ApplyFlag(RealtimeStateDto rt)
    {
        var kind = string.IsNullOrEmpty(rt.Flag) ? "None" : rt.Flag;
        FlagKind = kind;
        IsFlagNeutral = kind is "None" or "Formation";
        IsFlagGreen = kind == "Green";
        IsFlagYellow = kind == "Yellow";
        IsFlagRed = kind == "Red";
        IsFlagWhite = kind == "White";
        IsFlagChequered = kind == "Chequered";

        YellowS1 = IsFlagYellow && rt.YellowSector1;
        YellowS2 = IsFlagYellow && rt.YellowSector2;
        YellowS3 = IsFlagYellow && rt.YellowSector3;

        FlagText = kind switch
        {
            "Green" => "Зелёный флаг",
            "Yellow" => "Жёлтый флаг",
            "Red" => "Красный флаг",
            "White" => "Белый флаг",
            "Chequered" => "Клетчатый флаг",
            "Formation" => "Прогревочный круг",
            _ => "Сессия не идёт",
        };

        var detail = new List<string>();
        if (kind == "Yellow")
        {
            var sectors = new List<string>();
            if (rt.YellowSector1) sectors.Add("1");
            if (rt.YellowSector2) sectors.Add("2");
            if (rt.YellowSector3) sectors.Add("3");
            detail.Add(sectors.Count > 0 ? "сектор " + string.Join(", ", sectors) : "на трассе");
            detail.Add("не обгонять");
        }
        else if (kind == "White") detail.Add("медленная машина на трассе");
        else if (kind == "Red") detail.Add("сессия остановлена");
        else if (kind == "Chequered") detail.Add("финиш");
        if (!string.IsNullOrEmpty(rt.LastEvent)) detail.Add(rt.LastEvent);
        FlagDetail = string.Join(" · ", detail);

        HasPlayerFlag = !string.IsNullOrEmpty(rt.PlayerFlag);
        PlayerFlagText = rt.PlayerFlag switch
        {
            "Blue" => "Синий флаг: пропустите быстрого",
            "Black" => "Чёрный флаг",
            "Penalty" => "У вас штраф",
            "Orange" => "Оранжевый флаг: неисправность",
            _ => "",
        };

        ShowFlagBanner = IsFlagYellow || IsFlagRed || IsFlagWhite || IsFlagChequered || HasPlayerFlag;
    }

    private static string SessionTypeName(string? type) => type switch
    {
        "Race" => "Гонка",
        "Qualifying" => "Квалификация",
        "Superpole" => "Суперполь",
        "Practice" => "Практика",
        "Hotlap" => "Хотлап",
        "Hotstint" => "Хотстинт",
        "HotlapSuperpole" => "Хотлап-суперполь",
        "Replay" => "Повтор",
        _ => string.IsNullOrEmpty(type) ? "—" : type,
    };

    private static string PhaseName(string? phase) => phase switch
    {
        "Session" => "идёт",
        "FormationLap" => "прогревочный круг",
        "PreFormation" or "PreSession" or "Starting" => "подготовка",
        "SessionOver" => "завершена",
        "PostSession" or "ResultUI" => "результаты",
        _ => string.IsNullOrEmpty(phase) ? "—" : phase,
    };

    // ------------------------------------------------------------------ таблица позиций
    private readonly Dictionary<int, StandingsRow> _rows = new();

    [ObservableProperty] private StandingsRow? _selectedStanding;
    [ObservableProperty] private bool _hasSelectedStanding;

    /// <summary>Выбранная строка сменилась или обновились отрывы — перерисовать график отрыва.</summary>
    public event Action? GapTrendChanged;

    partial void OnSelectedStandingChanged(StandingsRow? value)
    {
        HasSelectedStanding = value != null;
        UpdateTrend();
        GapTrendChanged?.Invoke();
    }

    private void UpdateStandings(List<CarStateDto> cars, RealtimeStateDto rt)
    {
        var ordered = cars
            .OrderBy(c => c.Position == 0 ? int.MaxValue : c.Position)
            .ThenBy(c => c.CarIndex)
            .ToList();

        // убираем машины, которых больше нет в сессии
        var present = new HashSet<int>(ordered.Select(c => c.CarIndex));
        for (int i = Standings.Count - 1; i >= 0; i--)
        {
            if (present.Contains(Standings[i].CarIndex)) continue;
            if (ReferenceEquals(SelectedStanding, Standings[i])) SelectedStanding = null;
            _rows.Remove(Standings[i].CarIndex);
            Standings.RemoveAt(i);
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            var c = ordered[i];
            if (!_rows.TryGetValue(c.CarIndex, out var row))
            {
                row = new StandingsRow { CarIndex = c.CarIndex };
                _rows[c.CarIndex] = row;
            }

            FillRow(row, c, rt);

            var current = Standings.IndexOf(row);
            if (current < 0) Standings.Insert(Math.Min(i, Standings.Count), row);
            else if (current != i) Standings.Move(current, i);
        }
    }

    private static void FillRow(StandingsRow row, CarStateDto c, RealtimeStateDto rt)
    {
        row.Position = c.Position;
        row.RaceNumber = c.RaceNumber;
        row.TeamName = c.TeamName;
        row.Driver = c.Driver;
        row.CarModel = c.CarModelName;
        row.Laps = c.Laps;
        row.IsOurCar = c.IsOurCar;
        row.InPit = c.InPit;

        row.LastLap = FormatMs(c.LastLapMs);
        row.BestLap = FormatMs(c.BestLapMs);
        var sessionBest = rt.BestSessionLapMs;
        row.LastIsSessionBest = sessionBest.HasValue && c.LastLapMs == sessionBest;
        row.BestIsSessionBest = sessionBest.HasValue && c.BestLapMs == sessionBest;
        row.LastIsPersonalBest = !row.LastIsSessionBest && c.LastLapMs.HasValue && c.LastLapMs == c.BestLapMs;

        if (!rt.IsRace)
        {
            // квалификация и практика: отрывы не показываем, только время круга
            row.GapToLeader = "";
            row.IntervalAhead = "";
            row.IntervalBehind = "—";
            row.GapToOur = "—";
            row.GapToOurLabel = "";
            row.GapToOurAhead = false;
            row.GapToOurBehind = false;
            return;
        }

        row.GapToLeader = c.Position <= 1 ? "Лидер" : FormatGap(c.GapToLeaderSec, c.GapToLeaderLaps);
        row.IntervalAhead = c.Position <= 1 ? "—" : FormatGap(c.IntervalAheadSec, c.IntervalAheadLaps);
        row.IntervalBehind = c.IntervalBehindSec is double b ? "+" + b.ToString("F1", CultureInfo.CurrentCulture) : "—";

        if (c.IsOurCar)
        {
            row.GapToOur = "—";
            row.GapToOurLabel = "наша машина";
            row.GapToOurAhead = false;
            row.GapToOurBehind = false;
        }
        else if (c.GapToOurLaps != 0)
        {
            var laps = Math.Abs(c.GapToOurLaps);
            row.GapToOur = $"{laps} кр.";
            row.GapToOurAhead = c.GapToOurLaps > 0;
            row.GapToOurBehind = c.GapToOurLaps < 0;
            row.GapToOurLabel = row.GapToOurAhead ? "впереди нас" : "позади нас";
        }
        else if (c.GapToOurSec is double g)
        {
            row.GapToOur = Math.Abs(g).ToString("F1", CultureInfo.CurrentCulture) + " с";
            row.GapToOurAhead = g > 0;
            row.GapToOurBehind = g < 0;
            row.GapToOurLabel = g > 0 ? "впереди нас" : "позади нас";
        }
        else
        {
            row.GapToOur = "—";
            row.GapToOurLabel = "";
            row.GapToOurAhead = false;
            row.GapToOurBehind = false;
        }
    }

    private static string FormatGap(double? sec, int laps)
    {
        if (laps > 0) return $"+{laps} кр.";
        return sec is double s ? "+" + s.ToString("F1", CultureInfo.CurrentCulture) : "—";
    }

    // ------------------------------------------------------------------ история отрыва до нашей машины
    private readonly Dictionary<int, List<(double T, double Gap)>> _gapHistory = new();
    private double _lastHistoryAt = double.NegativeInfinity;
    private const double HistoryKeepSeconds = 900;

    /// <summary>Текущее время часов клиента — той же шкалы, что и точки истории.</summary>
    public double HistoryNow => _clockWatch.Elapsed.TotalSeconds;

    public IReadOnlyList<(double T, double Gap)> GetGapHistory(int carIndex)
        => _gapHistory.TryGetValue(carIndex, out var list) ? list : Array.Empty<(double T, double Gap)>();

    private void RecordGapHistory(List<CarStateDto> cars, bool isRace)
    {
        if (!isRace) return;
        var now = _clockWatch.Elapsed.TotalSeconds;
        if (now - _lastHistoryAt < 2) return;
        _lastHistoryAt = now;

        foreach (var c in cars)
        {
            if (c.IsOurCar || c.GapToOurSec is not double g) continue;
            if (!_gapHistory.TryGetValue(c.CarIndex, out var list))
            {
                list = new List<(double T, double Gap)>();
                _gapHistory[c.CarIndex] = list;
            }
            list.Add((now, g));

            var drop = 0;
            while (drop < list.Count && list[drop].T < now - HistoryKeepSeconds) drop++;
            if (drop > 0) list.RemoveRange(0, drop);
        }

        UpdateTrend();
        GapTrendChanged?.Invoke();
    }

    private void UpdateTrend()
    {
        var row = SelectedStanding;
        if (row == null) return;

        row.TrendText = "";
        row.TrendGood = false;
        row.TrendBad = false;
        if (row.IsOurCar || !IsRaceSession) return;

        var hist = GetGapHistory(row.CarIndex);
        if (hist.Count < 6)
        {
            row.TrendText = "Копим данные для тренда…";
            return;
        }

        var last = hist[hist.Count - 1];
        var oldIndex = hist.Count - 1;
        while (oldIndex > 0 && last.T - hist[oldIndex - 1].T <= 90) oldIndex--;
        var old = hist[oldIndex];
        var span = last.T - old.T;
        if (span < 20) return;

        var ahead = last.Gap > 0;
        var rate = (Math.Abs(last.Gap) - Math.Abs(old.Gap)) / span * 60; // секунд в минуту; < 0 — дистанция сокращается
        var abs = Math.Abs(rate).ToString("F1", CultureInfo.CurrentCulture);

        if (Math.Abs(rate) < 0.3)
        {
            row.TrendText = "Дистанция стабильна";
        }
        else if (rate < 0)
        {
            row.TrendText = ahead ? $"Вы догоняете: {abs} с/мин" : $"Он догоняет вас: {abs} с/мин";
            row.TrendGood = ahead;
            row.TrendBad = !ahead;
        }
        else
        {
            row.TrendText = ahead ? $"Он уходит: {abs} с/мин" : $"Вы уходите: {abs} с/мин";
            row.TrendGood = !ahead;
            row.TrendBad = ahead;
        }
    }

    // ------------------------------------------------------------------ радар
    [ObservableProperty] private bool _radarMirror = true;
    [ObservableProperty] private int _radarRangeIndex = 1;
    [ObservableProperty] private string _spotterText = "Свободно";
    [ObservableProperty] private bool _spotterDanger;
    [ObservableProperty] private string _radarStatus = "";

    public ObservableCollection<RadarNearItem> RadarNear { get; } = new();
    public RadarFrame? RadarFrameData { get; private set; }
    public double RadarRangeMeters => RadarRangeIndex switch { 0 => 20, 2 => 60, _ => 40 };

    /// <summary>Радар получил новый кадр (или поменялся масштаб) — перерисовать.</summary>
    public event Action? RadarUpdated;

    private readonly Dictionary<int, (double Dist, double T, double Closing)> _radarPrev = new();

    // Два последних снимка радара: между ними интерполируются позиции машин,
    // чтобы радар не «прыгал» на 10 Гц, а двигался плавно каждый кадр.
    private RadarFrame? _radarPrevFrame;
    private RadarFrame? _radarCurrFrame;
    private double _radarPrevAt;
    private double _radarCurrAt;

    partial void OnRadarRangeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RadarRangeMeters));
        if (_lastSnapshot != null) UpdateRadar(_lastSnapshot);
        SaveSettings();
    }

    partial void OnRadarMirrorChanged(bool value)
    {
        if (_lastSnapshot != null) UpdateRadar(_lastSnapshot);
        SaveSettings();
    }

    private void UpdateRadar(SessionSnapshotDto snapshot)
    {
        var cars = snapshot.Cars;
        var our = cars.FirstOrDefault(c => c.IsOurCar)
                  ?? (OurCarNumber > 0 ? cars.FirstOrDefault(c => c.RaceNumber == OurCarNumber) : null);

        if (our == null)
        {
            RadarFrameData = null;
            _radarPrevFrame = null;
            _radarCurrFrame = null;
            RadarNear.Clear();
            SpotterText = "—";
            SpotterDanger = false;
            RadarStatus = "Ваша машина не определена: задайте OurCarRaceNumber на сервере или номер в «Настройки»";
            RadarUpdated?.Invoke();
            return;
        }

        var frame = new RadarFrame { Number = our.RaceNumber.ToString(CultureInfo.InvariantCulture) };

        // Курс — единичный вектор по движению (считает сервер). Пока машина не ехала, он неизвестен.
        var hx = our.HeadingX;
        var hy = our.HeadingY;
        frame.HeadingKnown = hx != 0 || hy != 0;
        if (!frame.HeadingKnown) { hx = 0; hy = 1; }

        // «вправо» относительно курса. Если на радаре лево и право перепутаны, переключите «Зеркало» (RadarMirror).
        var rightX = RadarMirror ? -hy : hy;
        var rightY = RadarMirror ? hx : -hx;

        var range = RadarRangeMeters;
        var now = _clockWatch.Elapsed.TotalSeconds;
        var near = new List<(RadarCar Car, StandingsRowInfo Info)>();
        var seen = new HashSet<int>();

        foreach (var c in cars)
        {
            if (c.CarIndex == our.CarIndex) continue;
            if (c.WorldX == 0 && c.WorldY == 0) continue; // позиция ещё не пришла

            var dx = c.WorldX - our.WorldX;
            var dy = c.WorldY - our.WorldY;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > range * 1.2) continue;

            var x = dx * rightX + dy * rightY;
            var y = dx * hx + dy * hy;

            // поворот соперника относительно нас
            double angle = 0;
            if (c.HeadingX != 0 || c.HeadingY != 0)
            {
                var lx = c.HeadingX * rightX + c.HeadingY * rightY;
                var ly = c.HeadingX * hx + c.HeadingY * hy;
                angle = Math.Atan2(lx, ly) * 180.0 / Math.PI;
            }

            // скорость сближения
            double closing = 0;
            if (_radarPrev.TryGetValue(c.CarIndex, out var prev))
            {
                var dt = now - prev.T;
                closing = prev.Closing;
                if (dt >= 0.08 && dt < 1.5)
                {
                    var raw = (prev.Dist - dist) / dt * 3.6;
                    closing = prev.Closing * 0.7 + raw * 0.3;
                    _radarPrev[c.CarIndex] = (dist, now, closing);
                }
                else if (dt >= 1.5)
                {
                    closing = 0;
                    _radarPrev[c.CarIndex] = (dist, now, 0);
                }
            }
            else
            {
                _radarPrev[c.CarIndex] = (dist, now, 0);
            }
            seen.Add(c.CarIndex);

            var alongside = Math.Abs(y) < 5.5 && Math.Abs(x) < 4.5;
            var danger = alongside || dist < 6;
            var warn = !danger && dist < 12;

            if (alongside)
            {
                if (x < 0) frame.SpotterLeft = true; else frame.SpotterRight = true;
            }

            var car = new RadarCar
            {
                CarIndex = c.CarIndex,
                Number = c.RaceNumber,
                X = x,
                Y = y,
                Distance = dist,
                AngleDeg = angle,
                IsWarn = warn,
                IsDanger = danger,
            };
            frame.Cars.Add(car);
            near.Add((car, new StandingsRowInfo(c.Driver, alongside, closing)));
        }

        // забываем машины, которые ушли за пределы радара
        foreach (var key in _radarPrev.Keys.Where(k => !seen.Contains(k)).ToList())
            _radarPrev.Remove(key);

        _radarPrevFrame = _radarCurrFrame;
        _radarPrevAt = _radarCurrAt;
        _radarCurrFrame = frame;
        _radarCurrAt = now;

        RadarFrameData = frame;

        RadarNear.Clear();
        foreach (var (car, info) in near.OrderBy(n => n.Car.Distance).Take(6))
        {
            var place = info.Alongside ? (car.X < 0 ? "Слева рядом" : "Справа рядом")
                : car.Y >= 0 ? "Впереди" : "Сзади";
            var closingText = info.Closing > 3 ? $"сближается на {Math.Round(info.Closing)} км/ч"
                : info.Closing < -3 ? $"отдаляется на {Math.Round(-info.Closing)} км/ч"
                : "дистанция стабильна";
            RadarNear.Add(new RadarNearItem
            {
                Number = car.Number,
                Driver = info.Driver,
                Where = place,
                DistanceText = car.Distance.ToString("0.0", CultureInfo.CurrentCulture) + " м",
                ClosingText = closingText,
                IsWarn = car.IsWarn,
                IsDanger = car.IsDanger,
            });
        }

        SpotterDanger = frame.SpotterLeft || frame.SpotterRight;
        SpotterText = frame.SpotterLeft && frame.SpotterRight ? "Машины с обеих сторон!"
            : frame.SpotterLeft ? "Слева машина!"
            : frame.SpotterRight ? "Справа машина!"
            : "Свободно";
        RadarStatus = frame.HeadingKnown
            ? $"Радар {range:0} м вокруг машины №{frame.Number}"
            : "Направление определяется по движению: проедьте несколько метров";

        RadarUpdated?.Invoke();
    }

    /// <summary>
    /// Кадр радара для отрисовки: позиции интерполируются между двумя последними
    /// снимками сервера. Отрисовка идёт с задержкой в один интервал снимков,
    /// чтобы всегда был «следующий» кадр для плавного перехода.
    /// </summary>
    public RadarFrame? GetRadarFrameSmooth()
    {
        if (_radarCurrFrame == null) return null;
        if (_radarPrevFrame == null) return _radarCurrFrame;

        var interval = _radarCurrAt - _radarPrevAt;
        if (interval < 0.02 || interval > 0.5) return _radarCurrFrame;

        var now = _clockWatch.Elapsed.TotalSeconds;
        var delay = Math.Clamp(interval * 1.25, 0.08, 0.25);
        var t = now - delay;
        var f = (t - _radarPrevAt) / interval;

        if (f <= 0) return _radarPrevFrame;
        if (f >= 1) return _radarCurrFrame;
        return InterpolateRadar(_radarPrevFrame, _radarCurrFrame, f);
    }

    private static RadarFrame InterpolateRadar(RadarFrame from, RadarFrame to, double f)
    {
        var frame = new RadarFrame
        {
            Number = to.Number,
            HeadingKnown = to.HeadingKnown,
            SpotterLeft = to.SpotterLeft,
            SpotterRight = to.SpotterRight,
        };

        foreach (var car in to.Cars)
        {
            var prev = from.Cars.FirstOrDefault(p => p.CarIndex == car.CarIndex);
            if (prev == null)
            {
                frame.Cars.Add(car);
                continue;
            }

            frame.Cars.Add(new RadarCar
            {
                CarIndex = car.CarIndex,
                Number = car.Number,
                X = Lerp(prev.X, car.X, f),
                Y = Lerp(prev.Y, car.Y, f),
                Distance = Lerp(prev.Distance, car.Distance, f),
                AngleDeg = LerpAngle(prev.AngleDeg, car.AngleDeg, f),
                IsWarn = car.IsWarn,
                IsDanger = car.IsDanger,
            });
        }

        return frame;
    }

    private static double Lerp(double from, double to, double f) => from + (to - from) * f;

    private static double LerpAngle(double from, double to, double f)
    {
        // кратчайший путь по кругу: иначе при переходе через ±180° машина крутится
        var delta = (to - from + 540.0) % 360.0 - 180.0;
        return from + delta * f;
    }

    private readonly record struct StandingsRowInfo(string Driver, bool Alongside, double Closing);

    // ------------------------------------------------------------------ пилоты команды для агента
    public ObservableCollection<TeamDriverItem> TeamDrivers { get; } = new();
    public ICommand RefreshTeamDriversCommand { get; private set; } = null!;
    [ObservableProperty] private TeamDriverItem? _selectedTeamDriver;
    private DateTime _lastTeamDriversTry = DateTime.MinValue;

    partial void OnSelectedTeamDriverChanged(TeamDriverItem? value)
    {
        // Агент должен знать именно ИНДЕКС ПИЛОТА в машине, а не индекс машины
        if (value != null) SelectedDriverIndex = value.DriverIndex;
    }

    private async Task LoadTeamDriversAsync()
    {
        if (_api == null) return;
        _lastTeamDriversTry = DateTime.UtcNow;
        try
        {
            var list = await _api.GetTeamDriversAsync();
            var previous = SelectedTeamDriver?.DriverIndex;
            TeamDrivers.Clear();
            foreach (var d in list)
            {
                var name = $"{d.FirstName} {d.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = d.ShortName;
                TeamDrivers.Add(new TeamDriverItem
                {
                    DriverIndex = d.DriverIndex,
                    Name = string.IsNullOrWhiteSpace(d.ShortName) ? name : $"{name} ({d.ShortName})",
                    IsActiveNow = d.IsActiveNow,
                });
            }

            SelectedTeamDriver = TeamDrivers.FirstOrDefault(x => x.DriverIndex == previous)
                                 ?? TeamDrivers.FirstOrDefault(x => x.IsActiveNow)
                                 ?? TeamDrivers.FirstOrDefault();
        }
        catch (Exception ex)
        {
            DebugLog.Write("LoadTeamDrivers ERROR: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ адрес сервера
    /// <summary>Приводит ввод пользователя к виду http://host:port (порт по умолчанию 5261).</summary>
    public static string NormalizeServerUrl(string? input)
    {
        var s = (input ?? "").Trim().TrimEnd('/');
        if (s.Length == 0) s = "127.0.0.1";
        if (!s.Contains("://")) s = "http://" + s;

        var rest = s.Substring(s.IndexOf("://", StringComparison.Ordinal) + 3);
        var slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest.Substring(0, slash);
        if (!rest.Contains(':')) s += ":5261";
        return s;
    }

    private void SaveServerUrl(string url)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AccEndurance");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "server.url"), url);
        }
        catch (Exception ex)
        {
            DebugLog.Write("SaveServerUrl ERROR: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ телеметрия: шкалы
    [ObservableProperty] private double _gasPercent;
    [ObservableProperty] private double _brakePercent;
    [ObservableProperty] private double _steerLeftPercent;
    [ObservableProperty] private double _steerRightPercent;
}

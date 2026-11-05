using AccEndurance.Domain;
using AccEndurance.Domain.Entities;
using AccEndurance.Server.Models;
using AccEndurance.Shared.Dto;
using AccEndurance.Shared.Broadcasting;
using BC = AccEndurance.Shared.Broadcasting;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace AccEndurance.Server.Services;

public sealed class SessionStateService
{
    private readonly ILogger<SessionStateService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _lock = new();
    private readonly Dictionary<int, CarInfo> _entryList = new();
    private readonly Dictionary<int, CarStateDto> _carStates = new();
    private readonly List<StintDto> _stints = new();
    private readonly ConcurrentDictionary<string, string> _driverMapping = new();
    private readonly ConcurrentDictionary<string, DateTime> _driverLastSeen = new();

    // Сериализует запись стинтов в БД: без этого «открытие» и «закрытие» стинта,
    // запущенные подряд, могли оба увидеть Id == 0 и вставить две строки (и два Event).
    private readonly SemaphoreSlim _persistGate = new(1, 1);

    private readonly int? _configuredOurCarRaceNumber;
    private int? _autoOurCarIndex;

    private RealtimeStateDto _realtime = new();
    private StintDto? _currentStint;
    private string _activeDriverName = "";
    private bool _isSessionActive;
    private DateTime _lastStateBroadcast = DateTime.MinValue;
    private int? _dbEventId;
    private string _eventName = "ACC Endurance";
    private string _eventTrack = "";
    private int _currentSessionIndex;
    private string? _sessionKey;

    // Определение смысла второго поля RealtimeUpdate (см. ProcessRealtimeUpdate)
    private bool _timeProbeValid;
    private TimeSpan _probeSessionTime;
    private TimeSpan _probeSecondField;
    private bool _secondFieldIsRemaining = true;

    private readonly List<(float S, float X, float Y)> _currentLapPoints = new();
    private bool _lapDirty;
    private List<OutlinePointDto>? _recordedOutline;
    private long _outlineRevision;
    private int _lastRecordedLap = -1;

    public event Action<SessionSnapshotDto>? OnStateChanged;

    public IReadOnlyList<OutlinePointDto>? RecordedOutline => _recordedOutline;
    public long OutlineRevision => _outlineRevision;

    public bool IsSessionActive => _isSessionActive;
    public string ActiveDriverName => _activeDriverName;

    /// <summary>Копия списка стинтов (потокобезопасно). Для изменения используйте UpdateStintDetails.</summary>
    public IReadOnlyList<StintDto> Stints
    {
        get
        {
            lock (_lock)
                return _stints.Select(s => s.Clone()).ToList();
        }
    }

    public SessionStateService(
        ILogger<SessionStateService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _configuredOurCarRaceNumber = configuration.GetValue<int?>("Acc:OurCarRaceNumber");
    }

    public void UpdateEntryList(IEnumerable<CarInfo> cars)
    {
        lock (_lock)
        {
            _entryList.Clear();
            foreach (var car in cars)
                _entryList[car.CarIndex] = car;
        }
    }

    public void UpdateEntryListCar(CarInfo car)
    {
        lock (_lock)
        {
            _entryList[car.CarIndex] = car;
            UpdateDriverStates(car);
        }
    }

    public void RegisterDriver(string agentId, int driverIndex, string displayName)
    {
        _driverMapping[agentId] = $"{driverIndex}|{displayName}";
        _driverLastSeen[agentId] = DateTime.UtcNow;
        _logger.LogInformation("Driver registered: agentId={AgentId} → DriverIndex={DriverIndex} ({DisplayName})",
            agentId, driverIndex, displayName);
    }

    public bool TryGetRegisteredDriverIndex(string agentId, out int driverIndex, out string displayName)
    {
        driverIndex = -1;
        displayName = "";
        if (_driverMapping.TryGetValue(agentId, out var entry))
        {
            var parts = entry.Split('|');
            if (parts.Length >= 2 && int.TryParse(parts[0], out driverIndex))
            {
                displayName = parts[1];
                return true;
            }
        }
        return false;
    }

    public void ProcessRealtimeUpdate(RealtimeUpdate update)
    {
        lock (_lock)
        {
            var key = $"{update.EventIndex}/{update.SessionIndex}/{update.SessionType}";
            if (_sessionKey != null && _sessionKey != key)
                ResetForNewSession(key);
            _sessionKey = key;

            _realtime.SessionType = update.SessionType.ToString();
            _realtime.Phase = update.Phase.ToString();
            _currentSessionIndex = update.SessionIndex;
            _eventName = $"ACC {update.SessionType}";
            _realtime.SessionTime = update.SessionTime;
            _realtime.SessionEndTime = update.SessionEndTime;
            _realtime.RemainingTime = ComputeRemaining(update);
            _realtime.TimeOfDay = update.TimeOfDay;
            _realtime.AmbientTemp = update.AmbientTemp;
            _realtime.TrackTemp = update.TrackTemp;
            _realtime.RainLevel = update.RainLevel;
            _realtime.Wetness = update.Wetness;
            _realtime.Clouds = update.Clouds;
            _realtime.BestSessionLapMs = update.BestSessionLap?.LaptimeMs;

            _isSessionActive = update.Phase == BC.SessionPhase.Session || update.Phase == BC.SessionPhase.FormationLap;
        }
        MaybeBroadcast();
    }

    // Вызывать под _lock.
    // ACC присылает вторым значением время, и в разных описаниях протокола оно названо то
    // «SessionEndTime», то «RemainingTime». Раньше клиент считал «осталось» как EndTime - SessionTime;
    // если это поле уже «осталось», получалась отрицательная разность, и таймер не обновлялся вообще.
    // Определяем смысл по поведению: пока игровое время растёт, «осталось» убывает, а «время конца» — нет.
    private TimeSpan ComputeRemaining(RealtimeUpdate update)
    {
        var tolerance = TimeSpan.FromMilliseconds(10);
        if (_timeProbeValid && update.SessionTime > _probeSessionTime)
        {
            var delta = update.SessionEndTime - _probeSecondField;
            if (delta < -tolerance) _secondFieldIsRemaining = true;
            else if (delta > tolerance) _secondFieldIsRemaining = false;
        }
        _timeProbeValid = true;
        _probeSessionTime = update.SessionTime;
        _probeSecondField = update.SessionEndTime;

        if (_secondFieldIsRemaining)
            return update.SessionEndTime < TimeSpan.Zero ? TimeSpan.Zero : update.SessionEndTime;

        var left = update.SessionEndTime - update.SessionTime;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    // Вызывать под _lock. Новая сессия (другой Event/Session/тип) — старые стинты, позиции и
    // привязка к Event в БД больше не актуальны.
    private void ResetForNewSession(string newKey)
    {
        _logger.LogInformation("New broadcast session detected ({Old} → {New}), resetting in-memory state",
            _sessionKey, newKey);
        CloseCurrentStint();
        _stints.Clear();
        _carStates.Clear();
        _activeDriverName = "";
        _dbEventId = null;
        _lastRecordedLap = -1;
        _lapDirty = false;
        _currentLapPoints.Clear();
        _timeProbeValid = false;
    }

    public void ProcessRealtimeCarUpdate(RealtimeCarUpdate update)
    {
        lock (_lock)
        {
            if (!_carStates.TryGetValue(update.CarIndex, out var state))
            {
                string teamName = "", carModel = "";
                int raceNumber = 0;
                byte modelType = 0;
                if (_entryList.TryGetValue(update.CarIndex, out var carInfo))
                {
                    teamName = carInfo.TeamName;
                    carModel = CarModels.GetModelName(carInfo.CarModelType);
                    raceNumber = carInfo.RaceNumber;
                    modelType = carInfo.CarModelType;
                }
                state = new CarStateDto
                {
                    CarIndex = update.CarIndex,
                    TeamName = teamName,
                    CarModelType = modelType,
                    CarModelName = carModel,
                    RaceNumber = raceNumber,
                };
                _carStates[update.CarIndex] = state;
            }

            state.DriverIndex = update.DriverIndex;
            state.Position = update.Position;
            state.Laps = update.Laps;
            state.Location = update.CarLocation;
            state.SplinePosition = update.SplinePosition;
            state.Kmh = update.Kmh;
            state.SpeedMps = update.Kmh / 3.6;
            state.WorldX = update.WorldPosX;
            state.WorldY = update.WorldPosY;
            state.DeltaMs = update.Delta;
            // раньше эти поля никогда не заполнялись, и колонки «Последний/Лучший круг» были пустыми
            state.LastLapMs = ValidLapMs(update.LastLap);
            state.BestLapMs = ValidLapMs(update.BestSessionLap);

            state.CumulativeSpline = update.Laps + update.SplinePosition;

            if (_entryList.TryGetValue(update.CarIndex, out var car))
            {
                var currentDriver = update.DriverIndex < car.Drivers.Count
                    ? car.Drivers[update.DriverIndex]
                    : null;
                state.Driver = currentDriver?.ShortName ?? state.Driver;
                state.CarModelName = CarModels.GetModelName(car.CarModelType);
                state.TeamName = car.TeamName;
                state.RaceNumber = car.RaceNumber;
            }

            var isOurCar = IsOurCar(update.CarIndex);
            state.IsOurCar = isOurCar;

            if (isOurCar)
            {
                DetectStintChange(update, state);
                RecordCarPosition(update);
            }

            ComputeGaps(state);
        }

        MaybeBroadcast();
    }

    private static int? ValidLapMs(LapInfo? lap)
        => lap != null && lap.LaptimeMs > 0 && lap.LaptimeMs != int.MaxValue ? lap.LaptimeMs : null;

    private void DetectStintChange(RealtimeCarUpdate update, CarStateDto state)
    {
        _logger.LogDebug("Our car DriverIndex={DriverIndex} update", update.DriverIndex);

        if (_currentStint != null && _currentStint.DriverIndex == update.DriverIndex)
            return;

        CloseCurrentStint();

        var driverName = state.Driver;
        if (string.IsNullOrEmpty(driverName) && _entryList.TryGetValue(update.CarIndex, out var car))
        {
            var di = update.DriverIndex;
            driverName = (di >= 0 && di < car.Drivers.Count)
                ? car.Drivers[di].ShortName
                : $"Driver{update.DriverIndex}";
        }

        _currentStint = new StintDto
        {
            Driver = driverName ?? $"Driver{update.DriverIndex}",
            DriverIndex = update.DriverIndex,
            StartedAt = DateTime.UtcNow,
            StartSessionTime = _realtime.SessionTime,
            LapNumber = update.Laps,
        };
        _stints.Add(_currentStint);
        _activeDriverName = _currentStint.Driver;

        _logger.LogInformation("Stint opened: Driver={Driver} (DriverIndex={DriverIndex}) at Lap {Lap}",
            _activeDriverName, update.DriverIndex, update.Laps);

        _ = PersistStintAsync(_currentStint);
    }

    // Вызывать под _lock.
    private void CloseCurrentStint()
    {
        if (_currentStint == null) return;
        _currentStint.EndedAt = DateTime.UtcNow;
        _currentStint.EndSessionTime = _realtime.SessionTime;
        _ = PersistStintAsync(_currentStint);
        _currentStint = null;
    }

    /// <summary>
    /// Меняет заметки/план стинта и сохраняет изменения в БД
    /// (раньше PATCH менял только память, и после перезапуска сервера всё терялось).
    /// </summary>
    public StintDto? UpdateStintDetails(int id, string? notes, double? plannedFuelLiters, int? plannedLaps)
    {
        StintDto target;
        StintDto result;
        lock (_lock)
        {
            var found = id == 0 ? null : _stints.LastOrDefault(s => s.Id == id);
            if (found == null) return null;
            target = found;
            if (notes != null) target.Notes = notes;
            if (plannedFuelLiters.HasValue) target.PlannedFuelLiters = plannedFuelLiters;
            if (plannedLaps.HasValue) target.PlannedLaps = plannedLaps;
            result = target.Clone();
        }

        _ = PersistStintAsync(target);
        return result;
    }

    private async Task PersistStintAsync(StintDto stint)
    {
        await _persistGate.WaitAsync();
        try
        {
            StintDto snap;
            string eventName, eventTrack, sessionTypeName;
            int sessionIndex;
            int? eventId;
            lock (_lock)
            {
                snap = stint.Clone();
                eventName = _eventName;
                eventTrack = _eventTrack;
                sessionIndex = _currentSessionIndex;
                sessionTypeName = _realtime.SessionType;
                eventId = _dbEventId;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AccEnduranceDbContext>();

            if (eventId == null)
            {
                var evType = Enum.TryParse<Domain.Entities.SessionType>(sessionTypeName, out var parsed)
                             && Enum.IsDefined(parsed)
                    ? parsed
                    : Domain.Entities.SessionType.Race;

                var ev = new Domain.Entities.Event
                {
                    Name = eventName,
                    TrackName = eventTrack,
                    SessionIndex = sessionIndex,
                    SessionType = evType,
                    StartedAt = DateTime.UtcNow,
                };
                db.Events.Add(ev);
                await db.SaveChangesAsync();
                eventId = ev.Id;
                lock (_lock) { _dbEventId = eventId; }
                _logger.LogInformation("Event #{Id} created ({Name}/{Track})", ev.Id, ev.Name, ev.TrackName);
            }

            var driver = await db.Drivers.FirstOrDefaultAsync(d => d.ShortName == snap.Driver);
            if (driver == null)
            {
                driver = new Domain.Entities.Driver
                {
                    ShortName = snap.Driver,
                    FirstName = snap.Driver,
                    LastName = "",
                    AccDriverIndex = snap.DriverIndex,
                };
                db.Drivers.Add(driver);
                await db.SaveChangesAsync();
            }

            if (snap.Id == 0)
            {
                var entity = new Domain.Entities.Stint
                {
                    EventId = eventId.Value,
                    DriverId = driver.Id,
                    DriverIndex = snap.DriverIndex,
                    StartedAt = snap.StartedAt,
                    EndedAt = snap.EndedAt,
                    LapNumber = snap.LapNumber,
                    Notes = snap.Notes,
                    PlannedFuelLiters = snap.PlannedFuelLiters,
                    PlannedLaps = snap.PlannedLaps,
                };
                db.Stints.Add(entity);
                await db.SaveChangesAsync();
                lock (_lock) { stint.Id = entity.Id; }
                _logger.LogInformation("Stint {Driver} inserted as #{StintId} ({Start}→{End})",
                    snap.Driver, entity.Id, snap.StartedAt, snap.EndedAt);
            }
            else
            {
                var entity = await db.Stints.FirstOrDefaultAsync(s => s.Id == snap.Id);
                if (entity != null)
                {
                    entity.EndedAt = snap.EndedAt;
                    entity.LapNumber = snap.LapNumber;
                    entity.Notes = snap.Notes;
                    entity.PlannedFuelLiters = snap.PlannedFuelLiters;
                    entity.PlannedLaps = snap.PlannedLaps;
                    await db.SaveChangesAsync();
                    _logger.LogInformation("Stint #{StintId} saved ({Driver}, {Start}→{End})",
                        entity.Id, snap.Driver, snap.StartedAt, snap.EndedAt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist stint for {Driver}", stint.Driver);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private bool IsOurCar(ushort carIndex)
    {
        lock (_lock)
        {
            return _entryList.TryGetValue(carIndex, out var car) && IsOurCarInfo(car);
        }
    }

    // Вызывать под _lock.
    private bool IsOurCarInfo(CarInfo car)
    {
        // Раньше номер из Acc:OurCarRaceNumber читался, но нигде не использовался, а «нашей»
        // считалась ЛЮБАЯ машина с >= 3 пилотами — в выносливости таких много, и стинты
        // «прыгали» между машинами.
        if (_configuredOurCarRaceNumber is int raceNumber)
            return car.RaceNumber == raceNumber;

        if (_autoOurCarIndex is int idx)
            return idx == car.CarIndex;

        if (car.Drivers.Count >= 3)
        {
            _autoOurCarIndex = car.CarIndex;
            _logger.LogWarning(
                "Acc:OurCarRaceNumber не задан: «нашей» выбрана первая машина с >= 3 пилотами (#{RaceNumber}). " +
                "Задайте Acc:OurCarRaceNumber в appsettings.json.", car.RaceNumber);
            return true;
        }
        return false;
    }

    private void UpdateDriverStates(CarInfo car)
    {
        foreach (var driver in car.Drivers)
        {
            if (string.IsNullOrEmpty(driver.ShortName)) continue;
            _driverLastSeen[driver.ShortName] = DateTime.UtcNow;
        }
    }

    // Вызывать под _lock.
    private void RecordCarPosition(RealtimeCarUpdate update)
    {
        // Пит-лейн и въезд/выезд искажают контур: такой круг не годится для записи.
        if (update.CarLocation != BC.CarLocation.Track)
            _lapDirty = true;

        if (_lastRecordedLap < 0)
        {
            // Запись началась посреди круга — этот круг неполный.
            _lastRecordedLap = update.Laps;
            _lapDirty = true;
            return;
        }

        if (update.Laps != _lastRecordedLap)
        {
            if (update.Laps > _lastRecordedLap && !_lapDirty)
                FinalizeOutline(_lastRecordedLap);

            _currentLapPoints.Clear();
            _lapDirty = update.CarLocation != BC.CarLocation.Track;
            _lastRecordedLap = update.Laps;
        }

        if (update.CarLocation == BC.CarLocation.Track)
            _currentLapPoints.Add((update.SplinePosition, update.WorldPosX, update.WorldPosY));
    }

    private void FinalizeOutline(int completedLap)
    {
        // Точки пишутся по времени, а карта потом индексирует контур по SplinePosition.
        // Поэтому пересэмплируем контур равномерно по сплайну — иначе машинки «уезжают»
        // относительно реального положения (в медленных поворотах точек больше).
        var pts = _currentLapPoints.OrderBy(p => p.S).ToList();
        if (pts.Count < 50 || pts[0].S > 0.05f || pts[^1].S < 0.95f)
        {
            _logger.LogDebug("Outline: lap {Lap} discarded (points={Count})", completedLap, pts.Count);
            return;
        }

        double maxGap = 0;
        for (int i = 1; i < pts.Count; i++)
            maxGap = Math.Max(maxGap, pts[i].S - pts[i - 1].S);
        if (maxGap > 0.05)
        {
            _logger.LogDebug("Outline: lap {Lap} discarded (gap in spline coverage {Gap:F3})", completedLap, maxGap);
            return;
        }

        const int N = 1000;
        var sampled = new List<(double X, double Y)>(N);
        int j = 0;
        for (int i = 0; i < N; i++)
        {
            double s = i / (double)N;
            while (j < pts.Count - 2 && pts[j + 1].S < s) j++;
            var a = pts[j];
            var b = pts[j + 1];
            double span = b.S - a.S;
            double t = span <= 1e-9 ? 0 : Math.Clamp((s - a.S) / span, 0, 1);
            sampled.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }

        var minX = sampled.Min(p => p.X);
        var maxX = sampled.Max(p => p.X);
        var minY = sampled.Min(p => p.Y);
        var maxY = sampled.Max(p => p.Y);
        var extX = Math.Max(maxX - minX, 1e-3);
        var extY = Math.Max(maxY - minY, 1e-3);

        // равномерный масштаб (пропорции сохраняем), центрируем в канве 0..1
        var scale = 0.9 / Math.Max(extX, extY);
        var outline = new List<OutlinePointDto>(sampled.Count);
        foreach (var p in sampled)
        {
            outline.Add(new OutlinePointDto
            {
                X = Math.Round((p.X - minX - extX / 2) * scale + 0.5, 6),
                Y = Math.Round((p.Y - minY - extY / 2) * scale + 0.5, 6),
            });
        }

        _recordedOutline = outline;
        _outlineRevision++;
        _logger.LogInformation(
            "Recorded outline #{Rev}: lap {Lap} completed, {Count} points, extents {W:F0}m x {H:F0}m",
            _outlineRevision, completedLap, outline.Count, extX, extY);
    }

    // Вызывать под _lock.
    private void ComputeGaps(CarStateDto updatedCar)
    {
        // Сбрасываем старое значение: раньше при невозможности расчёта (круг отставания,
        // лидер, нет данных о трассе) в таблице оставался устаревший отрыв.
        updatedCar.GapSeconds = null;

        if (_carStates.Count < 2) return;

        var leadCar = _carStates.Values
            .Where(c => c.Position > 0)
            .OrderBy(c => c.Position)
            .FirstOrDefault();

        if (leadCar == null || leadCar.CarIndex == updatedCar.CarIndex) return;

        if (leadCar.Kmh > 0 && _realtime.TrackMeters > 0)
        {
            var gapFraction = leadCar.CumulativeSpline - updatedCar.CumulativeSpline;
            if (gapFraction >= 0 && gapFraction < 1.0)
            {
                updatedCar.GapSeconds = gapFraction * _realtime.TrackMeters / (leadCar.Kmh / 3.6);
            }
        }
    }

    private void MaybeBroadcast()
    {
        var now = DateTime.UtcNow;
        if (now - _lastStateBroadcast < TimeSpan.FromMilliseconds(250)) return;
        _lastStateBroadcast = now;
        OnStateChanged?.Invoke(BuildSnapshot());
    }

    /// <summary>
    /// Снимок состояния. Все объекты копируются под блокировкой: раньше наружу уходили
    /// живые ссылки, которые поток ACC менял прямо во время JSON-сериализации.
    /// </summary>
    public SessionSnapshotDto BuildSnapshot()
    {
        lock (_lock)
        {
            return new SessionSnapshotDto
            {
                Realtime = _realtime.Clone(),
                Cars = _carStates.Values
                    .OrderBy(c => c.Position)
                    .ThenBy(c => c.CarIndex)
                    .Select(c => c.Clone())
                    .ToList(),
                CurrentStint = _currentStint?.Clone(),
                Stints = _stints.Select(s => s.Clone()).ToList(),
            };
        }
    }

    public void SetTrackData(TrackData track)
    {
        lock (_lock)
        {
            _realtime.TrackMeters = track.TrackMeters;
            _realtime.TrackName = track.TrackName;
            _eventTrack = track.TrackName;
        }
    }

    public IReadOnlyList<DriverDto> GetTeamDrivers()
    {
        lock (_lock)
        {
            var result = new List<DriverDto>();
            foreach (var car in _entryList.Values.Where(IsOurCarInfo).ToList())
            {
                for (int di = 0; di < car.Drivers.Count; di++)
                {
                    var d = car.Drivers[di];
                    result.Add(new DriverDto
                    {
                        ExtId = result.Count,
                        FirstName = d.FirstName,
                        LastName = d.LastName,
                        ShortName = d.ShortName,
                        Nickname = "",
                        Category = d.Category.ToString(),
                        DriverIndex = di,
                        IsActiveNow = _currentStint != null && _currentStint.DriverIndex == di,
                    });
                }
            }
            return result;
        }
    }

    public IReadOnlyList<DriverDto> GetAllDrivers()
    {
        lock (_lock)
        {
            var result = new List<DriverDto>();
            foreach (var car in _entryList.Values.ToList())
            {
                var isOur = IsOurCarInfo(car);
                for (int di = 0; di < car.Drivers.Count; di++)
                {
                    var d = car.Drivers[di];
                    result.Add(new DriverDto
                    {
                        ExtId = car.CarIndex * 100 + di,
                        FirstName = d.FirstName,
                        LastName = d.LastName,
                        ShortName = d.ShortName,
                        Nickname = "",
                        Category = d.Category.ToString(),
                        DriverIndex = di,
                        // раньше «активным» помечался пилот с этим индексом в КАЖДОЙ машине
                        IsActiveNow = isOur && _currentStint != null && _currentStint.DriverIndex == di,
                    });
                }
            }
            return result;
        }
    }

    public void SavePendingStints()
    {
        lock (_lock) { CloseCurrentStint(); }
    }

    private readonly Dictionary<string, List<TelemetrySampleDto>> _pendingTelemetry = new();
    private readonly Dictionary<string, DateTime> _lastBatchAt = new();
    private readonly Stopwatch _telemetryFlushClock = Stopwatch.StartNew();

    /// <summary>
    /// Принимает пачку сэмплов от агента. Для «живых» графиков сэмплы отдаются клиентам ВСЕГДА, когда
    /// агент зарегистрирован. В БД они пишутся только когда сессия идёт и за рулём именно этот пилот.
    /// (Раньше при несовпадении пилота/сессии данные просто выбрасывались, и графики были бы пустыми.)
    /// </summary>
    public TelemetryAcceptResult AcceptTelemetryBatch(string agentId, IReadOnlyList<(float Gas, float Brake, float SteerAngle)> readings)
    {
        lock (_lock)
        {
            if (!TryGetRegisteredDriverIndex(agentId, out var agentDriverIndex, out var registeredName))
                return TelemetryAcceptResult.NotRegistered;

            var stint = _currentStint;
            var persist = _isSessionActive && stint != null && stint.DriverIndex == agentDriverIndex;

            // имя пилота для подписи на графиках
            string driverName;
            if (persist)
            {
                driverName = stint!.Driver;
            }
            else
            {
                var ourCar = _entryList.Values.FirstOrDefault(IsOurCarInfo);
                driverName = ourCar != null && agentDriverIndex >= 0 && agentDriverIndex < ourCar.Drivers.Count
                    ? ourCar.Drivers[agentDriverIndex].ShortName
                    : (!string.IsNullOrWhiteSpace(registeredName) ? registeredName : $"Driver {agentDriverIndex}");
            }

            // Агент присылает пачку несколько раз в секунду. Раскладываем сэмплы по интервалу с прошлой пачки,
            // а не ставим всем одну и ту же метку времени.
            var count = Math.Max(readings.Count, 1);
            var now = DateTime.UtcNow;
            var windowStart = _lastBatchAt.TryGetValue(agentId, out var prev) ? prev : now - TimeSpan.FromMilliseconds(250);
            if (now - windowStart > TimeSpan.FromSeconds(5)) windowStart = now - TimeSpan.FromSeconds(5);
            if (now <= windowStart) windowStart = now - TimeSpan.FromMilliseconds(count);
            _lastBatchAt[agentId] = now;
            var step = (now - windowStart) / count;

            var samples = new List<TelemetrySampleDto>(readings.Count);
            for (int i = 0; i < readings.Count; i++)
            {
                var r = readings[i];
                samples.Add(new TelemetrySampleDto
                {
                    DriverId = driverName,
                    StintId = persist ? stint!.Id : 0,
                    Timestamp = windowStart + step * (i + 1),
                    Gas = r.Gas,
                    Brake = r.Brake,
                    SteerAngle = r.SteerAngle,
                });
            }

            if (persist)
            {
                if (!_pendingTelemetry.TryGetValue(driverName, out var list))
                {
                    list = new List<TelemetrySampleDto>();
                    _pendingTelemetry[driverName] = list;
                }
                list.AddRange(samples);
            }

            return new TelemetryAcceptResult(true, persist, samples, driverName, agentDriverIndex);
        }
    }

    public IReadOnlyList<TelemetrySampleDto> DrainPendingTelemetry()
    {
        lock (_lock)
        {
            var all = _pendingTelemetry.Values.SelectMany(v => v).ToList();
            _pendingTelemetry.Clear();
            return all;
        }
    }

    /// <summary>Возвращает в очередь сэмплы, которые не удалось записать в БД (с ограничением размера).</summary>
    public void RequeueTelemetry(IReadOnlyList<TelemetrySampleDto> samples)
    {
        const int maxPending = 50_000;
        lock (_lock)
        {
            foreach (var group in samples.GroupBy(s => s.DriverId))
            {
                if (!_pendingTelemetry.TryGetValue(group.Key, out var list))
                {
                    list = new List<TelemetrySampleDto>();
                    _pendingTelemetry[group.Key] = list;
                }
                list.InsertRange(0, group);
            }

            var excess = _pendingTelemetry.Values.Sum(v => v.Count) - maxPending;
            foreach (var list in _pendingTelemetry.Values)
            {
                if (excess <= 0) break;
                var drop = Math.Min(excess, list.Count);
                list.RemoveRange(0, drop);
                excess -= drop;
            }
        }
    }
}

public sealed record TelemetryAcceptResult(
    bool Registered,
    bool Persisted,
    IReadOnlyList<TelemetrySampleDto> Samples,
    string Driver,
    int DriverIndex)
{
    public static TelemetryAcceptResult NotRegistered { get; } =
        new(false, false, Array.Empty<TelemetrySampleDto>(), "", -1);
}

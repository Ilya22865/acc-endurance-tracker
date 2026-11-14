using EnduranceHub.Domain;
using EnduranceHub.Domain.Entities;
using EnduranceHub.Server.Models;
using EnduranceHub.Shared.Dto;
using EnduranceHub.Shared.Broadcasting;
using BC = EnduranceHub.Shared.Broadcasting;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EnduranceHub.Server.Services;

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
    private string _eventName = "EnduranceHub";
    private string _eventTrack = "";
    private int _currentSessionIndex;
    private string? _sessionKey;

    // Определение смысла второго поля RealtimeUpdate (см. ProcessRealtimeUpdate)
    private bool _timeProbeValid;
    private TimeSpan _probeSessionTime;
    private TimeSpan _probeSecondField;
    private bool _secondFieldIsRemaining = true;

    // Часы сессии: сравниваем ход «игрового» времени с реальным (см. SessionClock)
    private readonly SessionClock _clock = new();
    private TimeSpan _remainingReal;
    private DateTime _lastClockAdvanceAt = DateTime.MinValue;
    private TimeSpan _lastElapsed;

    // История прохождения трассы каждой машиной — по ней считаются отрывы
    private readonly Dictionary<int, CarTimeline> _timelines = new();

    // Флаги: данные агента (память ACC на ПК пилота) и события Broadcasting
    private AgentFlagsState? _agentFlags;
    private DateTime _lastAccidentAt = DateTime.MinValue;
    private string _lastEventText = "";
    private DateTime _lastEventAt = DateTime.MinValue;

    // Живые давления/температуры шин и топливо от агента текущего пилота.
    private TyreStateDto? _liveTyres;
    // Последний план пит-стопа от споттера (подсказка пилоту в пит-меню).
    private PitPlanDto? _pitPlan;

    // Контур пишется с ЛЮБОЙ машины: у каждой свой буфер круга. Раньше писали только
    // с «нашей» машины, а её определение (OurCarRaceNumber/FocusedCarIndex) в онлайне
    // часто ошибочно — и карта не записывалась, даже если чистый круг проехал кто-то другой.
    private sealed class LapRecorder
    {
        public int LastLap = -1;
        public bool Dirty;
        public readonly List<(float S, float X, float Y)> Points = new();
    }

    private readonly Dictionary<ushort, LapRecorder> _lapRecorders = new();
    private List<OutlinePointDto>? _recordedOutline;
    private long _outlineRevision;
    private bool _outlineComplete;
    private string? _outlineTrackKey;

    private static readonly string OutlineDirectory = Path.Combine(
        EnduranceHub.Shared.AppPaths.LocalDataDirectory,
        "outlines");

    // Список машин переживает перезапуск сервера: без него в таблице позиций
    // оказываются нулевые номера и «Car #N» вместо названий команд.
    private static readonly string EntryListCachePath = Path.Combine(
        EnduranceHub.Shared.AppPaths.LocalDataDirectory,
        "entrylist-cache.json");

    private DateTime _lastEntryListCacheWriteUtc = DateTime.MinValue;
    private bool _entryListCacheDirty;
    private int _entryListCacheCarCount = -1;

    private sealed class EntryListCache
    {
        public string TrackName { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public List<CachedCar> Cars { get; set; } = new();
    }

    private sealed class CachedCar
    {
        public ushort CarIndex { get; set; }
        public byte CarModelType { get; set; }
        public string TeamName { get; set; } = "";
        public int RaceNumber { get; set; }
        public byte CupCategory { get; set; }
        public byte CurrentDriverIndex { get; set; }
        public ushort Nationality { get; set; }
        public List<CachedDriver> Drivers { get; set; } = new();
    }

    private sealed class CachedDriver
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public byte Category { get; set; }
        public ushort Nationality { get; set; }
    }

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
            {
                _entryList[car.CarIndex] = car;
                UpdateDriverStates(car);
                ApplyEntryListToState(car);
            }
            SaveEntryListCache(force: true);
        }
    }

    public void UpdateEntryListCar(CarInfo car)
    {
        lock (_lock)
        {
            _entryList[car.CarIndex] = car;
            UpdateDriverStates(car);
            // Список машин может прийти уже после первых RealtimeCarUpdate. Применяем его
            // к существующему состоянию сразу, не дожидаясь следующего пакета с трассы.
            ApplyEntryListToState(car);
            SaveEntryListCache();
        }
    }

    /// <summary>
    /// ACC прислал авторитетный список индексов: убираем машины, которых в сессии нет.
    /// Пустой список игнорируем — он приходит до загрузки сессии и не должен стирать кэш.
    /// </summary>
    public void PruneEntryList(IReadOnlyCollection<ushort> carIndices)
    {
        if (carIndices.Count == 0) return;

        lock (_lock)
        {
            var keep = carIndices.Select(i => (int)i).ToHashSet();
            foreach (var key in _entryList.Keys.Where(k => !keep.Contains(k)).ToList())
                _entryList.Remove(key);
            SaveEntryListCache(force: true);
        }
    }

    // Вызывать под _lock.
    // ACC присылает EntryListCar постоянно, поэтому кэш пишем не чаще раза в 5 секунд
    // (force=true — когда важно сохранить прямо сейчас).
    private void SaveEntryListCache(bool force = false)
    {
        var track = !string.IsNullOrWhiteSpace(_realtime.TrackName) ? _realtime.TrackName : _eventTrack;
        if (string.IsNullOrWhiteSpace(track) || _entryList.Count == 0) return;

        // Данные изменились; если писать сейчас рано — запишем при следующем
        // realtime-обновлении (см. ProcessRealtimeUpdate). Появление новой машины
        // в списке пишем сразу, иначе кэш может остаться неполным.
        _entryListCacheDirty = true;
        var newCar = _entryList.Count != _entryListCacheCarCount;
        if (!force && !newCar && DateTime.UtcNow - _lastEntryListCacheWriteUtc < TimeSpan.FromSeconds(5)) return;

        try
        {
            var cache = new EntryListCache
            {
                TrackName = track,
                SavedAtUtc = DateTime.UtcNow,
                Cars = _entryList.Values.Select(c => new CachedCar
                {
                    CarIndex = c.CarIndex,
                    CarModelType = c.CarModelType,
                    TeamName = c.TeamName,
                    RaceNumber = c.RaceNumber,
                    CupCategory = c.CupCategory,
                    CurrentDriverIndex = c.CurrentDriverIndex,
                    Nationality = c.Nationality,
                    Drivers = c.Drivers.Select(d => new CachedDriver
                    {
                        FirstName = d.FirstName,
                        LastName = d.LastName,
                        ShortName = d.ShortName,
                        Category = d.Category,
                        Nationality = d.Nationality,
                    }).ToList(),
                }).ToList(),
            };

            Directory.CreateDirectory(EnduranceHub.Shared.AppPaths.LocalDataDirectory);
            File.WriteAllText(EntryListCachePath, JsonSerializer.Serialize(cache));
            _lastEntryListCacheWriteUtc = DateTime.UtcNow;
            _entryListCacheDirty = false;
            _entryListCacheCarCount = _entryList.Count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save entry list cache");
        }
    }

    // Вызывать под _lock.
    private void RestoreEntryListCache(string trackName)
    {
        try
        {
            if (!File.Exists(EntryListCachePath)) return;

            var cache = JsonSerializer.Deserialize<EntryListCache>(File.ReadAllText(EntryListCachePath));
            if (cache?.Cars == null || cache.Cars.Count == 0) return;
            // Старый кэш может относиться к другой сессии на той же трассе.
            if (DateTime.UtcNow - cache.SavedAtUtc > TimeSpan.FromHours(12)) return;
            if (!string.Equals(cache.TrackName, trackName, StringComparison.OrdinalIgnoreCase)) return;

            var restored = 0;
            foreach (var cached in cache.Cars)
            {
                if (cached.Drivers.Count == 0) continue;

                var car = new CarInfo
                {
                    CarIndex = cached.CarIndex,
                    HasDetails = true,
                    CarModelType = cached.CarModelType,
                    TeamName = cached.TeamName,
                    RaceNumber = cached.RaceNumber,
                    CupCategory = cached.CupCategory,
                    CurrentDriverIndex = cached.CurrentDriverIndex,
                    Nationality = cached.Nationality,
                };
                foreach (var d in cached.Drivers)
                {
                    car.Drivers.Add(new DriverInfo
                    {
                        FirstName = d.FirstName,
                        LastName = d.LastName,
                        ShortName = d.ShortName,
                        Category = d.Category,
                        Nationality = d.Nationality,
                    });
                }

                _entryList[car.CarIndex] = car;
                ApplyEntryListToState(car);
                restored++;
            }

            if (restored > 0)
            {
                _logger.LogInformation(
                    "Restored entry list from cache: {Count} cars for {Track}",
                    restored, trackName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore entry list cache");
        }
    }

    // Вызывать под _lock.
    private void ApplyEntryListToState(CarInfo car)
    {
        if (!_carStates.TryGetValue(car.CarIndex, out var state)) return;

        if (!string.IsNullOrWhiteSpace(car.TeamName))
            state.TeamName = car.TeamName;
        // CarModelType = 0 — валидная модель (Porsche 991 GT3 R), поэтому ориентируемся
        // на факт получения EntryListCar, а не на «ненулевой» тип.
        if (car.HasDetails)
            state.CarModelName = CarModels.TryGetModelName(car.CarModelType, out var modelName) ? modelName : "";
        if (car.RaceNumber > 0)
            state.RaceNumber = car.RaceNumber;

        var di = state.DriverIndex;
        if (di >= 0 && di < car.Drivers.Count && !string.IsNullOrWhiteSpace(car.Drivers[di].ShortName))
            state.Driver = car.Drivers[di].ShortName;
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
        var found = _driverMapping.TryGetValue(agentId, out var entry);
        if (!found)
        {
            // Старые агенты регистрировались как «PC», а пачки слали как «PC:0» — сервер не находил регистрацию
            // и отвечал 409 на каждую пачку, поэтому телеметрия не доходила до клиента.
            var colon = agentId.LastIndexOf(':');
            if (colon > 0) found = _driverMapping.TryGetValue(agentId.Substring(0, colon), out entry);
        }
        if (found && entry != null)
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
            _realtime.IsRace = update.SessionType == BC.SessionType.Race;
            _realtime.TimeOfDay = update.TimeOfDay;
            _realtime.AmbientTemp = update.AmbientTemp;
            _realtime.TrackTemp = update.TrackTemp;
            _realtime.RainLevel = update.RainLevel;
            _realtime.Wetness = update.Wetness;
            _realtime.Clouds = update.Clouds;
            _realtime.BestSessionLapMs = update.BestSessionLap?.LaptimeMs;

            // ACC сообщает индекс машины, на которую смотрит камера. На ПК пилота это его
            // машина — гораздо надёжнее, чем прежняя эвристика «первая машина с 3+ пилотами»
            // (в эндурансе таких машин много, а у команды может быть всего 2 пилота).
            if (update.FocusedCarIndex >= 0 && _autoOurCarIndex != update.FocusedCarIndex)
            {
                _autoOurCarIndex = update.FocusedCarIndex;
                _logger.LogInformation(
                    "Focused car index {CarIndex} is treated as our car", update.FocusedCarIndex);
            }

            var wasActive = _isSessionActive;
            _isSessionActive = update.Phase == BC.SessionPhase.Session || update.Phase == BC.SessionPhase.FormationLap;

            // Сессия закончилась (клетчатый флаг/выход в меню): план пит-стопа и живые
            // давления больше не актуальны. При смене сессии их всё равно сбросит ResetForNewSession.
            if (wasActive && !_isSessionActive)
            {
                _pitPlan = null;
                _liveTyres = null;
            }
        }
        MaybeBroadcast();
    }

    // Вызывать под _lock.
    // ACC присылает вторым значением время, и в разных описаниях протокола оно названо то
    // «SessionEndTime», то «RemainingTime». Смысл определяем по поведению: пока «прошло» растёт,
    // «осталось» убывает на столько же, а «время конца» остаётся постоянным.
    // Затем проверяем, идут ли часы ACC в реальном времени: если игровое время ускорено,
    // таймер «до конца» бежал бы быстрее настоящего — делим на измеренный коэффициент.
    private TimeSpan ComputeRemaining(RealtimeUpdate update)
    {
        var now = DateTime.UtcNow;
        var e = update.SessionTime;
        var f = update.SessionEndTime;

        if (!_timeProbeValid || e < _probeSessionTime - TimeSpan.FromSeconds(1))
        {
            _timeProbeValid = true;
            _probeSessionTime = e;
            _probeSecondField = f;
            _clock.Reset();
        }
        else if (e > _probeSessionTime + TimeSpan.FromMilliseconds(50))
        {
            var dE = (e - _probeSessionTime).TotalMilliseconds;
            var dF = (f - _probeSecondField).TotalMilliseconds;
            if (Math.Abs(dF + dE) < dE * 0.5) _secondFieldIsRemaining = true;
            else if (Math.Abs(dF) < dE * 0.1) _secondFieldIsRemaining = false;
            _probeSessionTime = e;
            _probeSecondField = f;
        }

        if (e > _lastElapsed + TimeSpan.FromMilliseconds(10)) _lastClockAdvanceAt = now;
        _lastElapsed = e;
        _clock.Feed(e.TotalMilliseconds, now);

        var raw = _secondFieldIsRemaining ? f : f - e;
        if (raw < TimeSpan.Zero) raw = TimeSpan.Zero;

        var scale = _clock.Scale;
        _realtime.ClockScale = scale;
        _remainingReal = scale > 1.02 || scale < 0.98
            ? TimeSpan.FromMilliseconds(raw.TotalMilliseconds / scale)
            : raw;
        return _remainingReal;
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
        _lapRecorders.Clear();
        _timeProbeValid = false;
        _timelines.Clear();
        _clock.Reset();
        _lastAccidentAt = DateTime.MinValue;
        _lastEventText = "";
        _liveTyres = null;
        _pitPlan = null;
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
                    if (carInfo.HasDetails && CarModels.TryGetModelName(carInfo.CarModelType, out var entryModel))
                        carModel = entryModel;
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
            state.Yaw = update.Yaw;
            state.DeltaMs = update.Delta;
            // раньше эти поля никогда не заполнялись, и колонки «Последний/Лучший круг» были пустыми
            state.LastLapMs = ValidLapMs(update.LastLap);
            state.BestLapMs = ValidLapMs(update.BestSessionLap);

            if (!_timelines.TryGetValue(update.CarIndex, out var timeline))
            {
                timeline = new CarTimeline();
                _timelines[update.CarIndex] = timeline;
            }
            timeline.Feed(update.Laps, update.SplinePosition, DateTime.UtcNow.Ticks);
            timeline.UpdateHeading(update.WorldPosX, update.WorldPosY, update.Kmh);
            state.CumulativeSpline = timeline.CumNow;
            state.HeadingX = timeline.HeadingX;
            state.HeadingY = timeline.HeadingY;
            state.HeadingStale = timeline.HeadingStale;

            if (_entryList.TryGetValue(update.CarIndex, out var car))
            {
                var currentDriver = update.DriverIndex < car.Drivers.Count
                    ? car.Drivers[update.DriverIndex]
                    : null;
                // Не затираем уже известные данные пустыми/нулевыми значениями: в онлайне
                // EntryListCar может прийти позже, и раньше это обнуляло название команды.
                if (!string.IsNullOrWhiteSpace(currentDriver?.ShortName))
                    state.Driver = currentDriver!.ShortName;
                if (!string.IsNullOrWhiteSpace(car.TeamName))
                    state.TeamName = car.TeamName;
                if (car.HasDetails)
                    state.CarModelName = CarModels.TryGetModelName(car.CarModelType, out var modelName) ? modelName : "";
                if (car.RaceNumber > 0)
                    state.RaceNumber = car.RaceNumber;
            }

            var isOurCar = IsOurCar(update.CarIndex);
            state.IsOurCar = isOurCar;

            if (isOurCar)
                DetectStintChange(update, state);

            // Контур пишем с любой машины: наша может быть определена неверно,
            // а чистый круг в онлайне может проехать кто угодно.
            RecordCarPosition(update);

            // Отложенная запись кэша списка машин (см. SaveEntryListCache).
            if (_entryListCacheDirty)
                SaveEntryListCache();
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
        // План пит-стопа был адресован предыдущему пилоту — новому он уже не актуален.
        _pitPlan = null;

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
    /// Меняет пилота и время начала/конца стинта и сохраняет изменения в БД.
    /// Если стинта нет в памяти (например, после перезапуска сервера), строка обновляется
    /// напрямую в БД — раньше такой PATCH возвращал 404, и сохранение «не работало».
    /// </summary>
    public async Task<StintDto?> UpdateStintDetailsAsync(int id, string? driver, DateTime? startedAtUtc,
        DateTime? endedAtUtc, CancellationToken ct = default)
    {
        StintDto? target;
        lock (_lock)
        {
            target = _stints.LastOrDefault(s => s.Id == id);
            // Текущий стинт может ещё не получить Id от БД — тогда правим его напрямую.
            if (target == null && id == 0) target = _currentStint;
            if (target != null)
            {
                if (!string.IsNullOrWhiteSpace(driver)) target.Driver = driver.Trim();
                if (startedAtUtc.HasValue) target.StartedAt = EnsureUtc(startedAtUtc.Value);
                if (endedAtUtc.HasValue) target.EndedAt = EnsureUtc(endedAtUtc.Value);
            }
        }

        if (target != null)
        {
            await PersistStintAsync(target);
            return target.Clone();
        }

        if (id <= 0) return null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EnduranceHubDbContext>();
            var entity = await db.Stints
                .Include(s => s.Driver)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (entity == null) return null;

            if (!string.IsNullOrWhiteSpace(driver))
            {
                var name = driver.Trim();
                var driverEntity = await db.Drivers.FirstOrDefaultAsync(d => d.ShortName == name, ct);
                if (driverEntity == null)
                {
                    driverEntity = new Domain.Entities.Driver
                    {
                        ShortName = name,
                        FirstName = name,
                        LastName = "",
                        AccDriverIndex = entity.DriverIndex,
                    };
                    db.Drivers.Add(driverEntity);
                    await db.SaveChangesAsync(ct);
                }

                entity.DriverId = driverEntity.Id;
                entity.Driver = driverEntity;
            }

            if (startedAtUtc.HasValue) entity.StartedAt = EnsureUtc(startedAtUtc.Value);
            if (endedAtUtc.HasValue) entity.EndedAt = EnsureUtc(endedAtUtc.Value);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Stint #{StintId} saved directly to DB ({Driver}, {Start}→{End})",
                entity.Id, entity.Driver?.ShortName ?? "", entity.StartedAt, entity.EndedAt);

            return new StintDto
            {
                Id = entity.Id,
                Driver = entity.Driver?.ShortName ?? "",
                DriverId = entity.DriverId,
                DriverIndex = entity.DriverIndex,
                StartedAt = entity.StartedAt,
                EndedAt = entity.EndedAt,
                LapNumber = entity.LapNumber,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update stint #{StintId} in database", id);
            return null;
        }
    }

    /// <summary>
    /// Добавляет стинт вручную (план стинтов), не трогая текущий живой стинт.
    /// Возвращает null, если пилот или время не заданы либо запись не сохранилась.
    /// </summary>
    public async Task<StintDto?> CreateStintDetailsAsync(string? driver, DateTime startedAtUtc,
        DateTime? endedAtUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driver) || startedAtUtc == default) return null;

        var stint = new StintDto
        {
            Driver = driver.Trim(),
            // У ручной записи нет индекса пилота из игры.
            DriverIndex = -1,
            StartedAt = EnsureUtc(startedAtUtc),
            EndedAt = endedAtUtc.HasValue ? EnsureUtc(endedAtUtc.Value) : null,
        };

        await PersistStintAsync(stint);
        if (stint.Id == 0) return null;

        lock (_lock)
        {
            // Держим список в хронологическом порядке, чтобы план выглядел как план.
            var index = _stints.FindIndex(s => s.StartedAt > stint.StartedAt);
            if (index < 0) _stints.Add(stint);
            else _stints.Insert(index, stint);
        }

        _logger.LogInformation("Stint #{StintId} added manually ({Driver}, {Start}→{End})",
            stint.Id, stint.Driver, stint.StartedAt, stint.EndedAt);
        return stint.Clone();
    }

    /// <summary>
    /// Удаляет стинт из памяти и БД. Вместе со стинтом каскадно удаляются его
    /// телеметрические сэмплы, а круги остаются в истории (StintId становится null).
    /// </summary>
    public async Task<bool> DeleteStintAsync(int id, CancellationToken ct = default)
    {
        if (id <= 0) return false;

        var removedFromDb = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EnduranceHubDbContext>();
            var entity = await db.Stints.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (entity != null)
            {
                db.Stints.Remove(entity);
                await db.SaveChangesAsync(ct);
                removedFromDb = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete stint #{StintId} from database", id);
            return false;
        }

        var removedFromMemory = false;
        lock (_lock)
        {
            var target = _stints.FirstOrDefault(s => s.Id == id);
            if (target != null)
            {
                _stints.Remove(target);
                removedFromMemory = true;
            }

            // Если удалили живой стинт, он больше не должен закрываться/пересохраняться.
            if (_currentStint != null && _currentStint.Id == id)
            {
                _currentStint = null;
                _activeDriverName = "";
                removedFromMemory = true;
            }
        }

        // Запись могла ещё не попасть в БД (живой стинт), поэтому успехом считается
        // удаление хотя бы из одного места — иначе отвечаем 404.
        if (!removedFromDb && !removedFromMemory) return false;

        _logger.LogInformation("Stint #{StintId} deleted", id);
        return true;
    }

    /// <summary>
    /// Поднимает стинты последнего события из БД при старте сервера, чтобы история
    /// отображалась и её можно было редактировать, даже если сессия уже завершена.
    /// </summary>
    public async Task LoadLatestStintHistoryAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_isSessionActive || _stints.Count > 0) return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EnduranceHubDbContext>();

            var eventId = await db.Events
                .Where(e => e.Stints.Any())
                .OrderByDescending(e => e.StartedAt)
                .ThenByDescending(e => e.Id)
                .Select(e => (int?)e.Id)
                .FirstOrDefaultAsync(ct);
            if (eventId == null) return;

            var rows = await db.Stints
                .AsNoTracking()
                .Include(s => s.Driver)
                .Where(s => s.EventId == eventId.Value)
                .OrderBy(s => s.StartedAt)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);
            if (rows.Count == 0) return;

            lock (_lock)
            {
                if (_isSessionActive || _stints.Count > 0) return;
                foreach (var s in rows)
                {
                    _stints.Add(new StintDto
                    {
                        Id = s.Id,
                        Driver = s.Driver?.ShortName ?? "",
                        DriverId = s.DriverId,
                        DriverIndex = s.DriverIndex,
                        StartedAt = s.StartedAt,
                        EndedAt = s.EndedAt,
                        LapNumber = s.LapNumber,
                    });
                }

                // Ручные стинты, добавленные после перезапуска, должны попасть в то же событие,
                // а не создавать новое с пустым названием трассы.
                _dbEventId = eventId.Value;
            }

            _logger.LogInformation("Loaded {Count} stint(s) from latest event #{EventId}",
                rows.Count, eventId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stint history from database");
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

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
            var db = scope.ServiceProvider.GetRequiredService<EnduranceHubDbContext>();

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

                var entity = new Domain.Entities.Stint
                {
                    EventId = eventId.Value,
                    DriverId = driver.Id,
                    DriverIndex = snap.DriverIndex,
                    StartedAt = snap.StartedAt,
                    EndedAt = snap.EndedAt,
                    LapNumber = snap.LapNumber,
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
                    entity.DriverId = driver.Id;
                    entity.DriverIndex = snap.DriverIndex;
                    entity.StartedAt = snap.StartedAt;
                    entity.EndedAt = snap.EndedAt;
                    entity.LapNumber = snap.LapNumber;
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
        if (_outlineComplete) return;

        if (!_lapRecorders.TryGetValue(update.CarIndex, out var recorder))
        {
            recorder = new LapRecorder();
            _lapRecorders[update.CarIndex] = recorder;
        }

        // Пит-лейн и въезд/выезд искажают контур: такой круг не годится для записи.
        if (update.CarLocation != BC.CarLocation.Track)
            recorder.Dirty = true;

        if (recorder.LastLap < 0)
        {
            // Запись началась посреди круга — этот круг неполный.
            recorder.LastLap = update.Laps;
            recorder.Dirty = true;
            return;
        }

        if (update.Laps != recorder.LastLap)
        {
            if (update.Laps > recorder.LastLap && !recorder.Dirty)
                FinalizeOutline(update.CarIndex, recorder.LastLap, recorder.Points);

            recorder.Points.Clear();
            recorder.Dirty = update.CarLocation != BC.CarLocation.Track;
            recorder.LastLap = update.Laps;
        }

        if (update.CarLocation == BC.CarLocation.Track)
            recorder.Points.Add((update.SplinePosition, update.WorldPosX, update.WorldPosY));
    }

    private void FinalizeOutline(ushort carIndex, int completedLap, List<(float S, float X, float Y)> lapPoints)
    {
        if (_outlineComplete) return;

        // Точки пишутся по времени, а карта потом индексирует контур по SplinePosition.
        // Поэтому пересэмплируем контур равномерно по сплайну — иначе машинки «уезжают»
        // относительно реального положения (в медленных поворотах точек больше).
        var pts = lapPoints.OrderBy(p => p.S).ToList();
        if (pts.Count < 50 || pts[0].S > 0.05f || pts[^1].S < 0.95f)
        {
            _logger.LogDebug("Outline: car {Car} lap {Lap} discarded (points={Count})",
                carIndex, completedLap, pts.Count);
            return;
        }

        double maxGap = 0;
        for (int i = 1; i < pts.Count; i++)
            maxGap = Math.Max(maxGap, pts[i].S - pts[i - 1].S);
        if (maxGap > 0.05)
        {
            _logger.LogDebug("Outline: car {Car} lap {Lap} discarded (gap in spline coverage {Gap:F3})",
                carIndex, completedLap, maxGap);
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
        _outlineComplete = true;
        _lapRecorders.Clear();
        SaveOutlineToDisk(_outlineTrackKey ?? SanitizeTrackName(_realtime.TrackName), outline);
        _entryList.TryGetValue(carIndex, out var recordingCar);
        _logger.LogInformation(
            "Recorded outline #{Rev}: lap {Lap} completed by car #{Race}, {Count} points, extents {W:F0}m x {H:F0}m",
            _outlineRevision, completedLap, recordingCar?.RaceNumber ?? 0, outline.Count, extX, extY);
    }

    private static string SanitizeTrackName(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
            return "unknown";

        // «24H Nürburgring» → «24hnurburgring»: так же, как в старых сохранённых файлах.
        var normalized = trackName.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private List<OutlinePointDto>? LoadOutlineFromDisk(string key)
    {
        try
        {
            var path = Path.Combine(OutlineDirectory, $"outline-{key}.json");
            if (!File.Exists(path))
                return null;

            var points = JsonSerializer.Deserialize<List<OutlinePointDto>>(File.ReadAllText(path));
            if (points == null || points.Count < 50)
            {
                _logger.LogWarning("Saved outline {Path} is empty or too short; ignoring", path);
                return null;
            }

            return points;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load outline for track key {Key}", key);
            return null;
        }
    }

    private void SaveOutlineToDisk(string key, IReadOnlyList<OutlinePointDto> outline)
    {
        try
        {
            Directory.CreateDirectory(OutlineDirectory);
            var path = Path.Combine(OutlineDirectory, $"outline-{key}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(outline));
            _logger.LogInformation("Saved outline to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save outline for track key {Key}", key);
        }
    }

    // Вызывать под _lock. cars — копии, отсортированные по позиции.
    // Отрыв = через сколько секунд машина B проезжает ту точку трассы, где только что была машина A.
    // Раньше он считался как «доля круга × длина / скорость лидера», что при медленном лидере
    // (питы, поворот) давало заметно неверные цифры.
    private void ComputeAllGaps(List<CarStateDto> cars)
    {
        var chain = cars.Where(c => c.Position > 0 && _timelines.ContainsKey(c.CarIndex)).OrderBy(c => c.Position).ToList();
        if (chain.Count == 0) return;

        var leader = chain[0];
        var our = chain.FirstOrDefault(c => c.IsOurCar);
        var trackMeters = _realtime.TrackMeters;

        for (int i = 0; i < chain.Count; i++)
        {
            var car = chain[i];

            if (i > 0)
            {
                var (sec, laps) = GapBetween(leader, car, trackMeters);
                car.GapToLeaderSec = sec;
                car.GapToLeaderLaps = laps;
                car.GapSeconds = sec;

                var ahead = chain[i - 1];
                var (isec, ilaps) = GapBetween(ahead, car, trackMeters);
                car.IntervalAheadSec = isec;
                car.IntervalAheadLaps = ilaps;
                ahead.IntervalBehindSec = isec;
            }

            if (our != null && car.CarIndex != our.CarIndex)
            {
                var carAhead = car.CumulativeSpline >= our.CumulativeSpline;
                var (gsec, glaps) = carAhead
                    ? GapBetween(car, our, trackMeters)
                    : GapBetween(our, car, trackMeters);
                car.GapToOurSec = gsec.HasValue ? (carAhead ? gsec : -gsec) : null;
                car.GapToOurLaps = carAhead ? glaps : -glaps;
            }
        }
    }

    // Вызывать под _lock. front — тот, кто впереди; back — тот, кто сзади.
    private (double? Seconds, int Laps) GapBetween(CarStateDto front, CarStateDto back, int trackMeters)
    {
        var diff = front.CumulativeSpline - back.CumulativeSpline;
        if (diff < 0) diff = 0;
        if (diff >= 1.0) return (null, (int)Math.Floor(diff + 1e-6));

        if (_timelines.TryGetValue(front.CarIndex, out var ft)
            && _timelines.TryGetValue(back.CarIndex, out var bt)
            && ft.TryTimeAt(bt.CumNow, out var ticksFront))
        {
            var sec = (bt.LastTicks - ticksFront) / (double)TimeSpan.TicksPerSecond;
            return (Math.Max(0, sec), 0);
        }

        // Истории пока нет (сервер запущен недавно) — грубая оценка по скорости
        if (trackMeters > 0)
        {
            var speed = Math.Max(10.0, (front.SpeedMps + back.SpeedMps) / 2);
            return (diff * trackMeters / speed, 0);
        }
        return (null, 0);
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
            var realtime = _realtime.Clone();
            ApplyFlags(realtime);
            realtime.ClockRunning = DateTime.UtcNow - _lastClockAdvanceAt < TimeSpan.FromSeconds(3);
            realtime.LastEvent = DateTime.UtcNow - _lastEventAt < TimeSpan.FromSeconds(30) ? _lastEventText : "";

            var cars = _carStates.Values
                .OrderBy(c => c.Position)
                .ThenBy(c => c.CarIndex)
                .Select(c => c.Clone())
                .ToList();

            // Отрывы осмысленны только в гонке: в квалификации и практике позиция — по лучшему кругу
            if (realtime.IsRace) ComputeAllGaps(cars);

            return new SessionSnapshotDto
            {
                Realtime = realtime,
                Cars = cars,
                CurrentStint = _currentStint?.Clone(),
                Stints = _stints.Select(s => s.Clone()).ToList(),
                Tyres = _liveTyres?.Clone(),
                PitPlan = _pitPlan?.Clone(),
            };
        }
    }

    /// <summary>
    /// Публикует план пит-стопа от споттера. План хранится в памяти и попадает
    /// в каждый снимок состояния, поэтому его видят все клиенты, включая тех, кто
    /// подключился позже.
    /// </summary>
    public PitPlanDto SetPitPlan(PitPlanDto plan)
    {
        lock (_lock)
        {
            plan.Id = string.IsNullOrWhiteSpace(plan.Id) ? Guid.NewGuid().ToString("N") : plan.Id;
            plan.CreatedAtUtc = DateTime.UtcNow;
            _pitPlan = plan;
            return plan.Clone();
        }
    }

    /// <summary>Снимает план пит-стопа. С указанием id — только если он совпадает с текущим.</summary>
    public bool ClearPitPlan(string? id = null)
    {
        lock (_lock)
        {
            if (_pitPlan == null) return false;
            if (!string.IsNullOrWhiteSpace(id) &&
                !string.Equals(_pitPlan.Id, id, StringComparison.Ordinal))
                return false;
            _pitPlan = null;
            return true;
        }
    }

    public void SetTrackData(TrackData track)
    {
        lock (_lock)
        {
            _realtime.TrackMeters = track.TrackMeters;
            _realtime.TrackName = track.TrackName;
            _eventTrack = track.TrackName;

            // Контур записывается автоматически, но хранить его надо между запусками.
            // При смене трассы подхватываем сохранённый outline-<track>.json.
            var key = SanitizeTrackName(track.TrackName);
            if (string.Equals(_outlineTrackKey, key, StringComparison.Ordinal))
                return;

            _outlineTrackKey = key;
            _lapRecorders.Clear();
            _recordedOutline = LoadOutlineFromDisk(key);
            _outlineComplete = _recordedOutline != null;
            _outlineRevision++;

            if (_recordedOutline != null)
            {
                _logger.LogInformation(
                    "Loaded saved outline for track {Track}: {Count} points",
                    track.TrackName, _recordedOutline.Count);
            }
            else
            {
                _logger.LogInformation(
                    "Outline recording armed for track {Track}: waiting for a clean lap from any car",
                    track.TrackName);
            }

            // После перезапуска сервера ACC не всегда сразу отдаёт список машин.
            // Поднимаем его из кэша, чтобы номера и команды не превращались в нули.
            RestoreEntryListCache(track.TrackName);
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

    /// <summary>Флаги, прочитанные агентом из памяти ACC на ПК пилота.</summary>
    public void UpdateAgentFlags(string agentId, AgentFlagsState flags)
    {
        lock (_lock)
        {
            flags.At = DateTime.UtcNow;
            _agentFlags = flags;
        }
    }

    public void ProcessBroadcastingEvent(BroadcastingEvent evt)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            switch (evt.Type)
            {
                case BC.BroadcastingCarEvent.Accident:
                    _lastAccidentAt = now;
                    _lastEventText = evt.CarData != null ? $"Авария · #{evt.CarData.RaceNumber}" : "Авария на трассе";
                    _lastEventAt = now;
                    break;
                case BC.BroadcastingCarEvent.GreenFlag:
                    _lastAccidentAt = DateTime.MinValue;
                    _lastEventText = "Зелёный флаг";
                    _lastEventAt = now;
                    break;
                case BC.BroadcastingCarEvent.SessionOver:
                    _lastEventText = "Сессия завершена";
                    _lastEventAt = now;
                    break;
                case BC.BroadcastingCarEvent.PenaltyCommMsg:
                    _lastEventText = string.IsNullOrWhiteSpace(evt.Msg) ? "Штраф" : $"Штраф · {evt.Msg}";
                    _lastEventAt = now;
                    break;
            }
        }
    }

    // Вызывать под _lock.
    private void ApplyFlags(RealtimeStateDto rt)
    {
        var now = DateTime.UtcNow;
        var phase = rt.Phase;
        var inSession = phase == nameof(BC.SessionPhase.Session);

        var af = _agentFlags;
        if (af != null && now - af.At < TimeSpan.FromSeconds(4) && af.Status == 2)
        {
            rt.FlagSource = "agent";
            rt.YellowSector1 = af.Yellow1;
            rt.YellowSector2 = af.Yellow2;
            rt.YellowSector3 = af.Yellow3;
            var anyYellow = af.Yellow || af.Yellow1 || af.Yellow2 || af.Yellow3;
            rt.Flag = af.Red ? "Red"
                : af.Chequered ? "Chequered"
                : anyYellow ? "Yellow"
                : af.White ? "White"
                : af.Green || inSession ? "Green"
                : phase == nameof(BC.SessionPhase.FormationLap) ? "Formation"
                : "None";
            rt.PlayerFlag = af.PlayerFlag switch { 1 => "Blue", 3 => "Black", 6 => "Penalty", 8 => "Orange", _ => "" };
            return;
        }

        rt.PlayerFlag = "";
        rt.YellowSector1 = rt.YellowSector2 = rt.YellowSector3 = false;
        switch (phase)
        {
            case nameof(BC.SessionPhase.Session):
                if (now - _lastAccidentAt < TimeSpan.FromSeconds(25))
                {
                    rt.Flag = "Yellow";
                    rt.FlagSource = "event";
                }
                else
                {
                    rt.Flag = "Green";
                    rt.FlagSource = "phase";
                }
                break;
            case nameof(BC.SessionPhase.FormationLap):
                rt.Flag = "Formation";
                rt.FlagSource = "phase";
                break;
            case nameof(BC.SessionPhase.SessionOver):
            case nameof(BC.SessionPhase.PostSession):
            case nameof(BC.SessionPhase.ResultUI):
                rt.Flag = "Chequered";
                rt.FlagSource = "phase";
                break;
            default:
                rt.Flag = "None";
                rt.FlagSource = "phase";
                break;
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
    public TelemetryAcceptResult AcceptTelemetryBatch(string agentId,
        IReadOnlyList<(float Gas, float Brake, float SteerAngle)> readings, TyreStateDto? tyres = null)
    {
        lock (_lock)
        {
            if (!TryGetRegisteredDriverIndex(agentId, out var agentDriverIndex, out var registeredName))
                return TelemetryAcceptResult.NotRegistered;

            var stint = _currentStint;
            // Пока стинт не определён, не мешаем агенту (данные ещё не с чем сверять).
            // После смены пилота пакеты «старого» агента больше не попадают в эфир и в БД.
            var isCurrentDriver = stint == null || stint.DriverIndex == agentDriverIndex;
            var persist = _isSessionActive && stint != null && stint.DriverIndex == agentDriverIndex;

            // Давления нужны только от того, кто сейчас за рулём: иначе два агента
            // (например, на ПК напарника) будут перебивать показания друг друга.
            if (tyres != null && isCurrentDriver)
            {
                tyres.TimestampUtc = DateTime.UtcNow;
                _liveTyres = tyres;
            }

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
            // пустые пачки (агент шлёт только флаги) окно не сдвигают, иначе сэмплы сжались бы в последние миллисекунды
            if (readings.Count > 0) _lastBatchAt[agentId] = now;
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

            return new TelemetryAcceptResult(true, persist, isCurrentDriver, samples, driverName, agentDriverIndex);
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
    bool IsCurrentDriver,
    IReadOnlyList<TelemetrySampleDto> Samples,
    string Driver,
    int DriverIndex)
{
    public static TelemetryAcceptResult NotRegistered { get; } =
        new(false, false, false, Array.Empty<TelemetrySampleDto>(), "", -1);
}


/// <summary>Флаги из памяти ACC (страница Graphics), присланные агентом.</summary>
public sealed class AgentFlagsState
{
    public int Status { get; set; }          // 0 off, 1 replay, 2 live, 3 pause
    public int PlayerFlag { get; set; }      // 1 blue, 3 black, 6 penalty, 8 orange
    public bool Yellow { get; set; }
    public bool Yellow1 { get; set; }
    public bool Yellow2 { get; set; }
    public bool Yellow3 { get; set; }
    public bool White { get; set; }
    public bool Green { get; set; }
    public bool Chequered { get; set; }
    public bool Red { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// Сравнивает ход игровых часов ACC с реальным временем. Если игровое время ускорено (множитель времени на сервере),
/// «осталось до конца» в игровых единицах убывает быстрее настоящего — Scale показывает во сколько раз.
/// </summary>
internal sealed class SessionClock
{
    private bool _has;
    private double _elapsedAnchorMs;
    private DateTime _wallAnchor;

    public double Scale { get; private set; } = 1;

    public void Reset()
    {
        _has = false;
        Scale = 1;
    }

    public void Feed(double elapsedMs, DateTime nowUtc)
    {
        if (!_has || elapsedMs < _elapsedAnchorMs - 1000)
        {
            _has = true;
            _elapsedAnchorMs = elapsedMs;
            _wallAnchor = nowUtc;
            return;
        }

        var wallMs = (nowUtc - _wallAnchor).TotalMilliseconds;
        if (wallMs < 10_000) return;

        var gameMs = elapsedMs - _elapsedAnchorMs;
        if (gameMs > 0)
        {
            var k = gameMs / wallMs;
            var rounded = Math.Round(k);
            if (rounded >= 1 && Math.Abs(k - rounded) < 0.05 * rounded) k = rounded;
            else if (Math.Abs(k - 1) < 0.03) k = 1;
            Scale = Math.Clamp(k, 0.25, 60);
        }
        // gameMs <= 0 — пауза: коэффициент не меняем

        _elapsedAnchorMs = elapsedMs;
        _wallAnchor = nowUtc;
    }
}

/// <summary>История прохождения трассы одной машиной + курс по движению.</summary>
internal sealed class CarTimeline
{
    private const double SampleStep = 1.0 / 800;
    private const int MaxSamples = 2600; // ~3 круга

    private readonly List<double> _cum = new();
    private readonly List<long> _ticks = new();
    private bool _has;
    private double _lastSpline;
    private int _turns;
    private int _mismatchSamples;

    private bool _hasPos;
    private double _lastX, _lastY;

    public double CumNow { get; private set; }
    public long LastTicks { get; private set; }
    public double HeadingX { get; private set; }
    public double HeadingY { get; private set; }

    /// <summary>
    /// Курс устарел: машина почти стоит (или ещё не проехала достаточно метров).
    /// У стоящей машины направление по движению определить нельзя, поэтому радар
    /// не должен поворачивать её на случайный старый угол — рисуем вдоль нашей оси.
    /// </summary>
    public bool HeadingStale { get; private set; } = true;

    public void Feed(int laps, double spline, long ticks)
    {
        if (!_has)
        {
            _has = true;
            _turns = laps;
            _lastSpline = spline;
        }
        else
        {
            var d = spline - _lastSpline;
            if (d < -0.5) _turns++;
            else if (d > 0.5) _turns--;
            _lastSpline = spline;

            // ACC увеличивает счётчик кругов и сбрасывает сплайн не одновременно. Если расхождение держится
            // вдали от линии старт/финиш — доверяем счётчику кругов.
            if (_turns != laps && spline > 0.1 && spline < 0.9)
            {
                if (++_mismatchSamples > 20)
                {
                    _turns = laps;
                    _mismatchSamples = 0;
                    _cum.Clear();
                    _ticks.Clear();
                }
            }
            else
            {
                _mismatchSamples = 0;
            }
        }

        CumNow = _turns + spline;
        LastTicks = ticks;

        var n = _cum.Count;
        if (n > 0 && CumNow < _cum[n - 1] - 0.2)
        {
            // машину вернули назад (рестарт, телепорт в боксы) — старая история недостоверна
            _cum.Clear();
            _ticks.Clear();
            n = 0;
        }

        if (n == 0 || CumNow >= _cum[n - 1] + SampleStep)
        {
            _cum.Add(CumNow);
            _ticks.Add(ticks);
            if (_cum.Count > MaxSamples)
            {
                _cum.RemoveRange(0, 200);
                _ticks.RemoveRange(0, 200);
            }
        }
    }

    /// <summary>Когда эта машина была в точке cum (в долях круга с накоплением). false — точки нет в истории.</summary>
    public bool TryTimeAt(double cum, out long ticks)
    {
        ticks = 0;
        var n = _cum.Count;
        if (n < 2 || cum < _cum[0] || cum > _cum[n - 1]) return false;

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_cum[mid] <= cum) lo = mid; else hi = mid;
        }

        var span = _cum[hi] - _cum[lo];
        var t = span <= 1e-12 ? 0 : (cum - _cum[lo]) / span;
        ticks = _ticks[lo] + (long)((_ticks[hi] - _ticks[lo]) * t);
        return true;
    }

    /// <summary>Курс по смещению машины. Телепорты (боксы, рестарт) и медленное движение игнорируются.</summary>
    public void UpdateHeading(double x, double y, double kmh)
    {
        if (!_hasPos)
        {
            _hasPos = true;
            _lastX = x;
            _lastY = y;
            return;
        }

        var dx = x - _lastX;
        var dy = y - _lastY;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        // Стоящая машина: направление по движению неизвестно, даже если старая точка
        // ещё не накопила 1.5 м. Иначе в пит-лейне машины «смотрят» кто куда.
        if (kmh < 8) HeadingStale = true;
        if (dist < 1.5) return;                       // копим смещение
        _lastX = x;
        _lastY = y;
        if (dist > 60) return;                        // телепорт (боксы, рестарт)

        var nx = dx / dist;
        var ny = dy / dist;
        if (HeadingX == 0 && HeadingY == 0)
        {
            HeadingX = nx;
            HeadingY = ny;
            HeadingStale = false;
            return;
        }

        var hx = HeadingX * 0.5 + nx * 0.5;
        var hy = HeadingY * 0.5 + ny * 0.5;
        var len = Math.Sqrt(hx * hx + hy * hy);
        if (len < 1e-6) return;
        HeadingX = hx / len;
        HeadingY = hy / len;
        HeadingStale = false;
    }
}

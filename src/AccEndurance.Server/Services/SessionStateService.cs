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

    private RealtimeStateDto _realtime = new();
    private StintDto? _currentStint;
    private string _activeDriverName = "";
    private bool _isSessionActive;
    private DateTime _lastStateBroadcast = DateTime.MinValue;
    private int? _dbEventId;
    private string _eventName = "ACC Endurance";
    private string _eventTrack = "";
    private int _currentSessionIndex;

    private readonly List<(float X, float Y)> _currentLapPoints = new();
    private List<OutlinePointDto>? _recordedOutline;
    private long _outlineRevision;
    private int _lastRecordedLap = -1;

    public event Action<SessionSnapshotDto>? OnStateChanged;

    public IReadOnlyList<OutlinePointDto>? RecordedOutline => _recordedOutline;
    public long OutlineRevision => _outlineRevision;

    public bool IsSessionActive => _isSessionActive;
    public string ActiveDriverName => _activeDriverName;
    public IReadOnlyList<StintDto> Stints => _stints;

    public SessionStateService(ILogger<SessionStateService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
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
        _realtime.SessionType = update.SessionType.ToString();
        _realtime.Phase = update.Phase.ToString();
        _currentSessionIndex = update.SessionIndex;
        _eventName = $"ACC {update.SessionType}";
        _realtime.SessionTime = update.SessionTime;
        _realtime.SessionEndTime = update.SessionEndTime;
        _realtime.TimeOfDay = update.TimeOfDay;
        _realtime.AmbientTemp = update.AmbientTemp;
        _realtime.TrackTemp = update.TrackTemp;
        _realtime.RainLevel = update.RainLevel;
        _realtime.Wetness = update.Wetness;
        _realtime.Clouds = update.Clouds;
        _realtime.BestSessionLapMs = update.BestSessionLap?.LaptimeMs;

        _isSessionActive = update.Phase == BC.SessionPhase.RealtimeSession || update.Phase == BC.SessionPhase.FormationLap;
        MaybeBroadcast();
    }

    public void ProcessRealtimeCarUpdate(RealtimeCarUpdate update)
    {
        CarStateDto state;
        lock (_lock)
        {
            if (!_carStates.TryGetValue(update.CarIndex, out state))
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
            state.DeltaMs = update.Delta;

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
                DetectStintChange(update, state);

            if (isOurCar)
                RecordCarPosition(update);

            ComputeGaps(state);
        }

        MaybeBroadcast();
    }

    private void DetectStintChange(RealtimeCarUpdate update, CarStateDto state)
    {
        _logger.LogDebug("Our car DriverIndex={DriverIndex} update", update.DriverIndex);

        if (_currentStint != null && _currentStint.DriverIndex == update.DriverIndex)
            return;

        var previousDriverIndex = _currentStint?.DriverIndex ?? -1;
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

        _ = PersistStintAsync(_currentStint).ContinueWith(t =>
            _logger.LogError(t.Exception, "Failed to persist opened stint"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void CloseCurrentStint()
    {
        if (_currentStint == null) return;
        _currentStint.EndedAt = DateTime.UtcNow;
        _currentStint.EndSessionTime = _realtime.SessionTime;
        _ = PersistStintAsync(_currentStint).ContinueWith(t =>
            _logger.LogError(t.Exception, "Failed to persist closed stint"),
            TaskContinuationOptions.OnlyOnFaulted);
        _currentStint = null;
    }

    private async Task PersistStintAsync(StintDto stint)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AccEnduranceDbContext>();

            if (_dbEventId == null)
            {
                var ev = new Domain.Entities.Event
                {
                    Name = _eventName,
                    TrackName = _eventTrack,
                    SessionIndex = _currentSessionIndex,
                    SessionType = Domain.Entities.SessionType.Race,
                    StartedAt = DateTime.UtcNow,
                };
                db.Events.Add(ev);
                await db.SaveChangesAsync();
                _dbEventId = ev.Id;
                _logger.LogInformation("Event #{Id} created ({Name}/{Track})", ev.Id, ev.Name, ev.TrackName);
            }

            var driver = await db.Drivers.FirstOrDefaultAsync(d => d.ShortName == stint.Driver);
            if (driver == null)
            {
                driver = new Domain.Entities.Driver
                {
                    ShortName = stint.Driver,
                    FirstName = stint.Driver,
                    LastName = "",
                    AccDriverIndex = stint.DriverIndex,
                };
                db.Drivers.Add(driver);
                await db.SaveChangesAsync();
            }

            if (stint.Id == 0)
            {
                var entity = new Domain.Entities.Stint
                {
                    EventId = _dbEventId.Value,
                    DriverId = driver.Id,
                    DriverIndex = stint.DriverIndex,
                    StartedAt = stint.StartedAt,
                    EndedAt = stint.EndedAt,
                    LapNumber = stint.LapNumber,
                };
                db.Stints.Add(entity);
                await db.SaveChangesAsync();
                stint.Id = entity.Id;
                _logger.LogInformation("Stint {Driver} inserted as #{StintId} ({Start}→{End})",
                    stint.Driver, entity.Id, stint.StartedAt, stint.EndedAt);
            }
            else
            {
                var entity = await db.Stints.FirstOrDefaultAsync(s => s.Id == stint.Id);
                if (entity != null)
                {
                    entity.EndedAt = stint.EndedAt;
                    entity.LapNumber = stint.LapNumber;
                    await db.SaveChangesAsync();
                    _logger.LogInformation("Stint #{StintId} closed ({Driver}, {Start}→{End})",
                        entity.Id, stint.Driver, stint.StartedAt, stint.EndedAt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist stint for {Driver}", stint.Driver);
        }
    }

    private bool IsOurCar(ushort carIndex)
    {
        lock (_lock)
        {
            if (!_entryList.TryGetValue(carIndex, out var car)) return false;
            return car.Drivers.Count >= 3;
        }
    }

    private void UpdateDriverStates(CarInfo car)
    {
        foreach (var driver in car.Drivers)
        {
            if (string.IsNullOrEmpty(driver.ShortName)) continue;
            _driverLastSeen[driver.ShortName] = DateTime.UtcNow;
        }
    }

    private void RecordCarPosition(RealtimeCarUpdate update)
    {
        lock (_lock)
        {
            if (_lastRecordedLap < 0)
            {
                _lastRecordedLap = update.Laps;
                _currentLapPoints.Add((update.WorldPosX, update.WorldPosY));
                return;
            }

            if (update.Laps != _lastRecordedLap)
            {
                if (update.Laps > _lastRecordedLap && _currentLapPoints.Count >= 10)
                    FinalizeOutline(_lastRecordedLap);
                else
                    _currentLapPoints.Clear();
                _lastRecordedLap = update.Laps;
            }

            _currentLapPoints.Add((update.WorldPosX, update.WorldPosY));
        }
    }

    private void FinalizeOutline(int completedLap)
    {
        if (_currentLapPoints.Count < 10)
        {
            _currentLapPoints.Clear();
            return;
        }

        var minX = _currentLapPoints.Min(p => p.X);
        var maxX = _currentLapPoints.Max(p => p.X);
        var minY = _currentLapPoints.Min(p => p.Y);
        var maxY = _currentLapPoints.Max(p => p.Y);
        var extX = Math.Max(maxX - minX, 1e-3);
        var extY = Math.Max(maxY - minY, 1e-3);

        // равномерный масштаб (пропорции сохраняем), центрируем в канве 0..1
        var scale = 0.9 / Math.Max(extX, extY);
        var pts = new List<OutlinePointDto>(_currentLapPoints.Count);
        foreach (var p in _currentLapPoints)
        {
            pts.Add(new OutlinePointDto
            {
                X = Math.Round((p.X - minX - extX / 2) * scale + 0.5, 6),
                Y = Math.Round((p.Y - minY - extY / 2) * scale + 0.5, 6),
            });
        }

        pts = ThinOut(pts);
        _recordedOutline = pts;
        _outlineRevision++;
        _logger.LogInformation(
            "Recorded outline #{Rev}: lap {Lap} completed, {Count} points, extents {W:F0}m x {H:F0}m",
            _outlineRevision, completedLap, pts.Count, extX, extY);
        _currentLapPoints.Clear();
    }

    private static List<OutlinePointDto> ThinOut(List<OutlinePointDto> points)
    {
        const int maxPoints = 1200;
        if (points.Count <= maxPoints) return points;

        var result = new List<OutlinePointDto>(maxPoints);
        for (int i = 0; i < maxPoints; i++)
        {
            var src = (int)Math.Round(i * (points.Count - 1) / (double)(maxPoints - 1));
            result.Add(points[src]);
        }
        return result;
    }

    private void ComputeGaps(CarStateDto updatedCar)
    {
        if (_carStates.Count < 2) return;

        var leadCar = _carStates.Values
            .Where(c => c.Position > 0)
            .OrderBy(c => c.Position)
            .FirstOrDefault();

        if (leadCar == null || leadCar.CarIndex == updatedCar.CarIndex) return;

        if (leadCar.Kmh > 0)
        {
            var gapFraction = leadCar.CumulativeSpline - updatedCar.CumulativeSpline;
            if (gapFraction >= 0 && gapFraction < 1.0 && leadCar.Kmh > 0)
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

    public SessionSnapshotDto BuildSnapshot()
    {
        lock (_lock)
        {
            return new SessionSnapshotDto
            {
                Realtime = _realtime,
                Cars = _carStates.Values
                    .OrderBy(c => c.Position)
                    .ThenBy(c => c.CarIndex)
                    .ToList(),
                CurrentStint = _currentStint,
                Stints = _stints.ToList(),
            };
        }
    }

    public void SetTrackData(TrackData track)
    {
        _realtime.TrackMeters = track.TrackMeters;
        _realtime.TrackName = track.TrackName;
        _eventTrack = track.TrackName;
    }

    public IReadOnlyList<DriverDto> GetTeamDrivers()
    {
        lock (_lock)
        {
            var result = new List<DriverDto>();
            foreach (var car in _entryList.Values.Where(c => c.Drivers.Count >= 3))
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
            foreach (var car in _entryList.Values)
            {
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
                        IsActiveNow = _currentStint != null
                            && _currentStint.DriverIndex == di
                            && car.Drivers.Count >= 3,
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
    private readonly Stopwatch _telemetryFlushClock = Stopwatch.StartNew();

    public IReadOnlyList<TelemetrySampleDto> AcceptTelemetryBatch(string agentId, IReadOnlyList<(float Gas, float Brake, float SteerAngle)> readings)
    {
        if (!_isSessionActive) return Array.Empty<TelemetrySampleDto>();
        if (!TryGetRegisteredDriverIndex(agentId, out var agentDriverIndex, out _))
            return Array.Empty<TelemetrySampleDto>();
        if (_currentStint == null || _currentStint.DriverIndex != agentDriverIndex)
            return Array.Empty<TelemetrySampleDto>();

        var accepted = new List<TelemetrySampleDto>(readings.Count);
        var now = DateTime.UtcNow;
        foreach (var r in readings)
        {
            accepted.Add(new TelemetrySampleDto
            {
                DriverId = _currentStint.Driver,
                Timestamp = now,
                Gas = r.Gas,
                Brake = r.Brake,
                SteerAngle = r.SteerAngle,
            });
        }

        lock (_lock)
        {
            if (!_pendingTelemetry.TryGetValue(_currentStint.Driver, out var list))
            {
                list = new List<TelemetrySampleDto>();
                _pendingTelemetry[_currentStint.Driver] = list;
            }
            list.AddRange(accepted);
        }

        return accepted;
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
}
using AccEndurance.Shared.Broadcasting;

namespace AccEndurance.Shared.Dto;

public sealed class OutlinePointDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class CarStateDto
{
    public int CarIndex { get; set; }
    public int RaceNumber { get; set; }
    public string TeamName { get; set; } = "";
    public byte CarModelType { get; set; }
    public string CarModelName { get; set; } = "";
    public string Driver { get; set; } = "";
    public int DriverIndex { get; set; }
    public ushort Position { get; set; }
    public double? GapSeconds { get; set; }
    public int Laps { get; set; }
    public CarLocation Location { get; set; }
    public double SplinePosition { get; set; }
    public double CumulativeSpline { get; set; }
    public double Kmh { get; set; }
    public double SpeedMps { get; set; }
    public int? LastLapMs { get; set; }
    public int? BestLapMs { get; set; }
    public int? DeltaMs { get; set; }

    public bool IsOurCar { get; set; }

    // Мировые координаты машины (метры) — нужны радару для расчёта положения соперников.
    public double WorldX { get; set; }
    public double WorldY { get; set; }

    // Все поля — значения/строки, поэтому поверхностной копии достаточно.
    public CarStateDto Clone() => (CarStateDto)MemberwiseClone();
}

public sealed class RealtimeStateDto
{
    public string SessionType { get; set; } = "";
    public string Phase { get; set; } = "";
    public TimeSpan SessionTime { get; set; }
    public TimeSpan SessionEndTime { get; set; }
    /// <summary>Оставшееся время сессии (считается сервером, см. SessionStateService).</summary>
    public TimeSpan RemainingTime { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public byte AmbientTemp { get; set; }
    public byte TrackTemp { get; set; }
    public double RainLevel { get; set; }
    public double Wetness { get; set; }
    public double Clouds { get; set; }
    public int? BestSessionLapMs { get; set; }
    public int TrackMeters { get; set; }
    public string TrackName { get; set; } = "";

    public RealtimeStateDto Clone() => (RealtimeStateDto)MemberwiseClone();
}

public sealed class StintDto
{
    public int Id { get; set; } = 0;
    public string Driver { get; set; } = "";
    public int DriverId { get; set; }
    public int DriverIndex { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public TimeSpan StartSessionTime { get; set; }
    public TimeSpan? EndSessionTime { get; set; }
    public int LapNumber { get; set; }
    public string? Notes { get; set; }
    public double? PlannedFuelLiters { get; set; }
    public int? PlannedLaps { get; set; }

    public StintDto Clone() => (StintDto)MemberwiseClone();
}

public sealed class TelemetrySampleDto
{
    public string DriverId { get; set; } = "";
    public int StintId { get; set; }
    public DateTime Timestamp { get; set; }
    public float Gas { get; set; }
    public float Brake { get; set; }
    public float SteerAngle { get; set; }
}

/// <summary>Пачка сэмплов телеметрии, которую сервер сразу рассылает клиентам по SignalR.</summary>
public sealed class TelemetryBatchDto
{
    public string Driver { get; set; } = "";
    public int DriverIndex { get; set; }
    public List<TelemetrySampleDto> Samples { get; set; } = new();
}

public sealed class DriverDto
{
    public int ExtId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Category { get; set; } = "";
    public int DriverIndex { get; set; }
    public bool IsActiveNow { get; set; }
}

public sealed class EntryListDto
{
    public List<DriverDto> Drivers { get; set; } = new();
    public List<CarEntryDto> Cars { get; set; } = new();
}

public sealed class CarEntryDto
{
    public int CarIndex { get; set; }
    public int RaceNumber { get; set; }
    public string TeamName { get; set; } = "";
    public byte CarModelType { get; set; }
    public string CarModelName { get; set; } = "";
    public List<DriverDto> Drivers { get; set; } = new();
}

public sealed class SessionSnapshotDto
{
    public RealtimeStateDto Realtime { get; set; } = new();
    public List<CarStateDto> Cars { get; set; } = new();
    public StintDto? CurrentStint { get; set; }
    public List<StintDto> Stints { get; set; } = new();
}

public sealed class SetupCarDto
{
    public string CarFolder { get; set; } = "";
    public List<string> Tracks { get; set; } = new();
}

public sealed class SetupTrackDto
{
    public string TrackFolder { get; set; } = "";
    public List<string> Files { get; set; } = new();
}

public sealed class SetupFileDto
{
    public string FileName { get; set; } = "";
    public DateTime ModifiedAt { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class SetupBaseDto
{
    public string BasePath { get; set; } = "";
    public bool Exists { get; set; }
}
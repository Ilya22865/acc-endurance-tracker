using EnduranceHub.Shared.Broadcasting;

namespace EnduranceHub.Shared.Dto;

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

    /// <summary>Угол рыскания из ACC (рад). Справочно; курс для радара считается по движению (HeadingX/Y).</summary>
    public double Yaw { get; set; }

    /// <summary>Единичный вектор курса машины в мировых координатах (по движению). (0;0) — пока неизвестен.</summary>
    public double HeadingX { get; set; }
    public double HeadingY { get; set; }

    /// <summary>Курс устарел (машина стоит): для радара её нельзя поворачивать на старый угол.</summary>
    public bool HeadingStale { get; set; }

    public bool InPit => Location == CarLocation.Pitlane || Location == CarLocation.PitEntry || Location == CarLocation.PitExit;

    // --- отрывы (только в гонке; считаются сервером по времени прохождения одних и тех же точек трассы) ---
    /// <summary>Отрыв до лидера, с. null — отстаёт на круг и более или данных пока нет.</summary>
    public double? GapToLeaderSec { get; set; }
    /// <summary>Сколько полных кругов потеряно относительно лидера.</summary>
    public int GapToLeaderLaps { get; set; }
    /// <summary>Интервал до машины впереди (по позиции в гонке), с.</summary>
    public double? IntervalAheadSec { get; set; }
    public int IntervalAheadLaps { get; set; }
    /// <summary>Интервал до машины позади, с.</summary>
    public double? IntervalBehindSec { get; set; }
    /// <summary>Отрыв до НАШЕЙ машины, с. Больше 0 — соперник впереди нас, меньше 0 — позади.</summary>
    public double? GapToOurSec { get; set; }
    /// <summary>Разница в полных кругах с нашей машиной (знак как у GapToOurSec).</summary>
    public int GapToOurLaps { get; set; }

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

    /// <summary>Гонка (отрывы показываются только в ней).</summary>
    public bool IsRace { get; set; }
    /// <summary>Во сколько раз часы сессии ACC идут быстрее реального времени (1 — как обычно). RemainingTime уже пересчитан в реальное время.</summary>
    public double ClockScale { get; set; } = 1;
    /// <summary>false — часы ACC стоят (пауза/нет обновлений).</summary>
    public bool ClockRunning { get; set; } = true;

    /// <summary>None, Formation, Green, Yellow, White, Red, Chequered.</summary>
    public string Flag { get; set; } = "None";
    public bool YellowSector1 { get; set; }
    public bool YellowSector2 { get; set; }
    public bool YellowSector3 { get; set; }
    /// <summary>Личный флаг пилота: Blue, Black, Penalty, Orange (пусто — нет). Приходит только от агента.</summary>
    public string PlayerFlag { get; set; } = "";
    /// <summary>agent — из памяти ACC на ПК пилота; event — по событию Accident; phase — по фазе сессии.</summary>
    public string FlagSource { get; set; } = "";
    public string LastEvent { get; set; } = "";

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

    public StintDto Clone() => (StintDto)MemberwiseClone();
}

/// <summary>Текст ошибки, который сервер возвращает при неудачном создании/изменении стинта.</summary>
public sealed class StintErrorDto
{
    public string Error { get; set; } = "";
}

/// <summary>
/// Живые давления и температуры шин, а также топливо из страницы Physics памяти ACC.
/// Приходят от агента, запущенного на ПК пилота, и нужны калькулятору давления и споттеру.
/// </summary>
public sealed class TyreStateDto
{
    public DateTime TimestampUtc { get; set; }
    /// <summary>Топливо в баке, л.</summary>
    public float FuelLiters { get; set; }
    /// <summary>Давления, psi: переднее левое, переднее правое, заднее левое, заднее правое.</summary>
    public float[] Pressures { get; set; } = new float[4];
    /// <summary>Температура ядра шин, °C (тот же порядок).</summary>
    public float[] CoreTemperatures { get; set; } = new float[4];

    public TyreStateDto Clone() => new()
    {
        TimestampUtc = TimestampUtc,
        FuelLiters = FuelLiters,
        Pressures = (float[])Pressures.Clone(),
        CoreTemperatures = (float[])CoreTemperatures.Clone(),
    };
}

/// <summary>
/// План пит-стопа, который споттер собирает во время гонки и показывает пилоту.
/// ACC не даёт менять пит-стратегию чужой машины по сети, поэтому план — это
/// подсказка: пилот вводит эти значения в пит-меню (MFD).
/// </summary>
public sealed class PitPlanDto
{
    public string Id { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Пилот, которому адресован план (кто сейчас за рулём).</summary>
    public string Driver { get; set; } = "";
    /// <summary>Сколько топлива добавить, л (0 — не заправлять).</summary>
    public double FuelLiters { get; set; }
    public bool ChangeTyres { get; set; }
    /// <summary>«Сухо» или «Дождь».</summary>
    public string TyreCompound { get; set; } = "";
    /// <summary>Холодные давления для пит-меню, psi (ПЛ/ПП/ЗЛ/ЗП). Пусто — не менять.</summary>
    public double[] Pressures { get; set; } = Array.Empty<double>();
    public bool Repair { get; set; }
    public bool DriverSwap { get; set; }
    /// <summary>Короткий комментарий споттера.</summary>
    public string Note { get; set; } = "";

    public PitPlanDto Clone() => (PitPlanDto)MemberwiseClone();
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
    /// <summary>Живые давления/температуры шин и топливо нашего автомобиля (от агента).</summary>
    public TyreStateDto? Tyres { get; set; }
    /// <summary>Последний план пит-стопа от споттера (null — плана нет).</summary>
    public PitPlanDto? PitPlan { get; set; }
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
namespace AccEndurance.Shared.Broadcasting;

public enum OutboundMessageTypes : byte
{
    RegisterCommandApplication = 1,
    UnregisterCommandApplication = 9,
    RequestEntryList = 10,
    RequestTrackData = 11,
}

public enum InboundMessageTypes : byte
{
    RegistrationResult = 1,
    RealtimeUpdate = 2,
    RealtimeCarUpdate = 3,
    EntryList = 4,
    TrackData = 5,
    EntryListCar = 6,
    BroadcastingEvent = 7,
}

public enum SessionPhase : byte
{
    None = 0,
    Starting,
    PreFormation,
    FormationLap,
    RealtimeSession,
    SessionEnded,
    SessionAbandoned,
}

public enum SessionType : byte
{
    Practice = 0,
    Qualifying,
    Race,
    Hotlap,
    TimeAttack,
    Drift,
    Drag,
    Hotstint,
    HotlapSuperpole,
    Replay,
}

public enum CarLocation : byte
{
    Track = 0,
    Pitlane,
    PitEntry,
    PitExit,
}

public enum BroadcastingCarEvent : byte
{
    GreenFlag,
    SessionOver,
    PenaltyCommMsg,
    Accident,
    Lapped,
    LastLap,
    BlueFlag,
    Retired,
    FlagBlack,
    FlagBlue,
    FlagWhite,
    FlagCheckered,
    FlagYellow,
    FlagGreen,
    Max,
}

public sealed class LapInfo
{
    public int LaptimeMs { get; set; }
    public ushort CarIndex { get; set; }
    public ushort DriverIndex { get; set; }
    public IReadOnlyList<int?> Splits { get; set; } = Array.Empty<int?>();
    public bool IsInvalid { get; set; }
    public bool IsValidForBest { get; set; }
    public string Type { get; set; } = "Regular";

    public TimeSpan? Laptime => LaptimeMs == int.MaxValue ? null : TimeSpan.FromMilliseconds(LaptimeMs);
}

public sealed class DriverInfo
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public byte Category { get; set; }
    public ushort Nationality { get; set; }

    public override string ToString() => $"{FirstName} {LastName} ({ShortName})";
}

public sealed class CarInfo
{
    public ushort CarIndex { get; init; }
    public byte CarModelType { get; set; }
    public string TeamName { get; set; } = "";
    public int RaceNumber { get; set; }
    public byte CupCategory { get; set; }
    public byte CurrentDriverIndex { get; set; }
    public ushort Nationality { get; set; }
    public List<DriverInfo> Drivers { get; } = new();

    public DriverInfo? CurrentDriver => Drivers.Count > CurrentDriverIndex ? Drivers[CurrentDriverIndex] : null;
}

public sealed class RealtimeUpdate
{
    public int EventIndex { get; set; }
    public int SessionIndex { get; set; }
    public SessionType SessionType { get; set; }
    public SessionPhase Phase { get; set; }
    public TimeSpan SessionTime { get; set; }
    public TimeSpan SessionEndTime { get; set; }
    public int FocusedCarIndex { get; set; }
    public string ActiveCameraSet { get; set; } = "";
    public string ActiveCamera { get; set; } = "";
    public string CurrentHudPage { get; set; } = "";
    public TimeSpan TimeOfDay { get; set; }
    public byte AmbientTemp { get; set; }
    public byte TrackTemp { get; set; }
    public float Clouds { get; set; }
    public float RainLevel { get; set; }
    public float Wetness { get; set; }
    public LapInfo? BestSessionLap { get; set; }
}

public sealed class RealtimeCarUpdate
{
    public ushort CarIndex { get; set; }
    public ushort DriverIndex { get; set; }
    public byte DriverCount { get; set; }
    public short Gear { get; set; }
    public float WorldPosX { get; set; }
    public float WorldPosY { get; set; }
    public float Yaw { get; set; }
    public CarLocation CarLocation { get; set; }
    public ushort Kmh { get; set; }
    public ushort Position { get; set; }
    public ushort CupPosition { get; set; }
    public ushort TrackPosition { get; set; }
    public float SplinePosition { get; set; }
    public ushort Laps { get; set; }
    public int Delta { get; set; }
    public LapInfo? BestSessionLap { get; set; }
    public LapInfo? LastLap { get; set; }
    public LapInfo? CurrentLap { get; set; }
}

public sealed class TrackData
{
    public string TrackName { get; set; } = "";
    public int TrackId { get; set; }
    public int TrackMeters { get; set; }
}

public sealed class BroadcastingEvent
{
    public BroadcastingCarEvent Type { get; set; }
    public string Msg { get; set; } = "";
    public int TimeMs { get; set; }
    public int CarId { get; set; }
    public CarInfo? CarData { get; set; }
}
namespace AccEndurance.Domain.Entities;

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

public class Event
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string TrackName { get; set; } = "";
    public int EventIndex { get; set; }
    public int SessionIndex { get; set; }
    public SessionType SessionType { get; set; }
    public SessionPhase Phase { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public int? CarId { get; set; }
    public Car? Car { get; set; }

    public ICollection<Stint> Stints { get; set; } = new List<Stint>();
    public ICollection<Lap> Laps { get; set; } = new List<Lap>();
}
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccEndurance.Client.ViewModels;

/// <summary>Строка списка «Рядом» на радаре.</summary>
public sealed class RadarNearItem
{
    public int Number { get; init; }
    public string Driver { get; init; } = "";
    public string Where { get; init; } = "";
    public string DistanceText { get; init; } = "";
    public string ClosingText { get; init; } = "";
    public bool IsWarn { get; init; }
    public bool IsDanger { get; init; }
}

/// <summary>Одна машина на радаре в системе координат нашей: X — вправо, Y — вперёд (метры).</summary>
public sealed class RadarCar
{
    public int CarIndex { get; init; }
    public int Number { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Distance { get; init; }
    /// <summary>Поворот относительно нашей машины, градусы по часовой (0 — едет в ту же сторону).</summary>
    public double AngleDeg { get; init; }
    public bool IsWarn { get; init; }
    public bool IsDanger { get; init; }
}

public sealed class RadarFrame
{
    public List<RadarCar> Cars { get; } = new();
    public bool SpotterLeft { get; set; }
    public bool SpotterRight { get; set; }
    public bool HeadingKnown { get; set; }
    public string Number { get; set; } = "";
}

namespace EnduranceHub.Client.ViewModels;

/// <summary>Пилот нашей команды для выбора в окне «Агент».</summary>
public sealed class TeamDriverItem
{
    public int DriverIndex { get; init; }
    public string Name { get; init; } = "";
    public bool IsActiveNow { get; init; }
    public override string ToString() => Name;
}

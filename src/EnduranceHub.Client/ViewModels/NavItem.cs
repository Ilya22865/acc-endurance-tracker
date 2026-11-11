using Avalonia.Media;

namespace EnduranceHub.Client.ViewModels;

public sealed class NavItem
{
    public string Title { get; init; } = "";
    /// <summary>Иконка 24×24, рисуется линией (Stroke).</summary>
    public Geometry Icon { get; init; } = Geometry.Parse("M3 12h18");
    /// <summary>Над пунктом рисуется разделитель (начало группы «Система»).</summary>
    public bool IsGroupStart { get; init; }
}

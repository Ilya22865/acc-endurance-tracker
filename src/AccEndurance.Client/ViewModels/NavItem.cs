using Avalonia.Media;

namespace AccEndurance.Client.ViewModels;

public sealed class NavItem
{
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public IBrush Accent { get; init; } = Brushes.Gray;
}
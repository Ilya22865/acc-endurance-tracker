using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EnduranceHub.Client.ViewModels;

public sealed partial class SetupSection : ObservableObject
{
    public string Title { get; init; } = "";
    public IBrush Accent { get; init; } = Brushes.Gray;
    public ObservableCollection<SetupValue> Items { get; init; } = new();
}

public sealed class SetupValue
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
}
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccEndurance.Client.ViewModels;

public sealed partial class SetupFileRow : ObservableObject
{
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _modifiedText = "";
    [ObservableProperty] private string _sizeText = "";
}
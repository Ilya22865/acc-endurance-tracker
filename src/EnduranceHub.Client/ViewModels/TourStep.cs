namespace EnduranceHub.Client.ViewModels;

/// <summary>
/// Один шаг обучающего тура. <paramref name="Target"/> — имя элемента интерфейса,
/// который подсвечивается (<c>nav</c> — пункт навигации, <c>connect</c> — поле
/// подключения); <c>null</c> — шаг без подсветки, карточка по центру окна.
/// </summary>
public sealed record TourStep(
    string Title,
    string Body,
    string? Target = null,
    int? NavIndex = null,
    int? PageIndex = null);

namespace EnduranceHub.Client.Services;

/// <summary>
/// Фиксированные пути к Server и Agent, которые поставляются вместе с клиентом.
/// Установщик Velopack кладёт все файлы в одну папку приложения, поэтому пути
/// всегда считаются от каталога, где запущен клиент.
/// </summary>
public static class EmbeddedServicePaths
{
    public static string ServerExePath => GetExecutablePath("Server", "EnduranceHub.Server");

    public static string AgentExePath => GetExecutablePath("Agent", "EnduranceHub.Agent");

    private static string GetExecutablePath(string folder, string executableName)
    {
        var fileName = OperatingSystem.IsWindows() ? executableName + ".exe" : executableName;
        return Path.Combine(AppContext.BaseDirectory, folder, fileName);
    }
}

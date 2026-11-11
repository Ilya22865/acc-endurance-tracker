namespace EnduranceHub.Shared;

/// <summary>
/// Единые пути к пользовательским данным приложения.
/// После переименования AccEndurance → EnduranceHub данные из старой папки
/// копируются один раз, чтобы не потерять настройки, контуры трасс и автозапуск.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "EnduranceHub";
    private const string LegacyFolderName = "AccEndurance";

    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string RoamingDataDirectory { get; } = Path.Combine(
        GetRoamingRoot(),
        AppFolderName);

    /// <summary>
    /// Копирует старые папки данных в новые, если новых ещё нет. Существующие
    /// новые папки не трогаем — пользователь мог уже что-то изменить.
    /// </summary>
    public static void MigrateLegacyData()
    {
        MigrateDirectory(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LegacyFolderName),
            LocalDataDirectory);

        MigrateDirectory(
            Path.Combine(GetRoamingRoot(), LegacyFolderName),
            RoamingDataDirectory);
    }

    private static string GetRoamingRoot()
        => OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static void MigrateDirectory(string source, string target)
    {
        try
        {
            if (!Directory.Exists(source) || Directory.Exists(target)) return;
            CopyDirectory(source, target);
        }
        catch
        {
            // Миграция не должна мешать запуску: при ошибке просто останемся с пустыми настройками.
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}

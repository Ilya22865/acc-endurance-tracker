using EnduranceHub.Client.Diagnostics;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace EnduranceHub.Client.Services;

/// <summary>
/// Checks the public GitHub Releases feed and silently installs a newer version.
/// Updates are only applied to Velopack-installed builds; local developer builds
/// and portable/unmanaged launches are left untouched.
/// </summary>
public static class UpdateService
{
    private const string ConfigFileName = "update-source.json";
    private const string DefaultRepository = "https://github.com/Ilya22865/acc-endurance-tracker";
    private static int _started;

    public static async Task CheckAndApplyAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            var repository = ReadRepositoryUrl();
            var manager = new UpdateManager(
                new GithubSource(repository, accessToken: null, prerelease: false));

            if (!manager.IsInstalled)
            {
                DebugLog.Write("Updater skipped: this is not a Velopack-installed build.");
                return;
            }

            DebugLog.Write("Checking GitHub Releases for updates...");
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                DebugLog.Write("Application is up to date.");
                return;
            }

            DebugLog.Write("Update found. Downloading in background...");
            await manager.DownloadUpdatesAsync(update);

            DebugLog.Write("Update downloaded. Applying update and restarting...");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            // An unavailable GitHub API or network must never prevent the app from starting.
            DebugLog.Write("UPDATE ERROR: " + ex);
        }
    }

    private static string ReadRepositoryUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("GitHubRepository", out var value))
                {
                    var repository = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(repository))
                        return repository.TrimEnd('/');
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("Could not read update-source.json: " + ex.Message);
        }

        return DefaultRepository;
    }
}

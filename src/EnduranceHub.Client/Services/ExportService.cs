using System.Globalization;
using System.Text;
using System.Text.Json;
using EnduranceHub.Shared.Dto;
using EnduranceHub.Client.Diagnostics;

namespace EnduranceHub.Client.Services;

public sealed class ExportService
{
    public async Task<bool> ExportToJsonAsync(SessionSnapshotDto snapshot, string filePath)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(snapshot, options);
            await File.WriteAllTextAsync(filePath, json);

            DebugLog.Write($"Export: JSON saved to {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Export JSON ERROR: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ExportToCsvAsync(SessionSnapshotDto snapshot, string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            // Export session info
            await ExportSessionInfoCsv(snapshot, Path.Combine(directoryPath, "session.csv"));

            // Export standings
            await ExportStandingsCsv(snapshot.Cars, Path.Combine(directoryPath, "standings.csv"));

            // Export stints
            await ExportStintsCsv(snapshot.Stints, snapshot.CurrentStint, Path.Combine(directoryPath, "stints.csv"));

            // Export lap times
            await ExportLapTimesCsv(snapshot.Cars, Path.Combine(directoryPath, "lap_times.csv"));

            DebugLog.Write($"Export: CSV files saved to {directoryPath}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Export CSV ERROR: {ex.Message}");
            return false;
        }
    }

    private async Task ExportSessionInfoCsv(SessionSnapshotDto snapshot, string filePath)
    {
        var sb = new StringBuilder();
        var rt = snapshot.Realtime;

        sb.AppendLine("Property,Value");
        sb.AppendLine($"Session Type,{EscapeCsv(rt.SessionType)}");
        sb.AppendLine($"Track Name,{EscapeCsv(rt.TrackName)}");
        sb.AppendLine($"Track Length (m),{rt.TrackMeters}");
        sb.AppendLine($"Phase,{EscapeCsv(rt.Phase)}");
        sb.AppendLine($"Session Time,{rt.SessionTime}");
        sb.AppendLine($"Session End Time,{rt.SessionEndTime}");
        sb.AppendLine($"Time of Day,{rt.TimeOfDay}");
        sb.AppendLine($"Ambient Temp (°C),{rt.AmbientTemp}");
        sb.AppendLine($"Track Temp (°C),{rt.TrackTemp}");
        sb.AppendLine($"Rain Level,{rt.RainLevel:F3}");
        sb.AppendLine($"Wetness,{rt.Wetness:F3}");
        sb.AppendLine($"Clouds,{rt.Clouds:F3}");
        sb.AppendLine($"Best Session Lap (ms),{rt.BestSessionLapMs}");

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private async Task ExportStandingsCsv(List<CarStateDto> cars, string filePath)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Position,Race Number,Team Name,Driver,Car Model,Laps,Gap (s),Last Lap (ms),Best Lap (ms),Speed (km/h),Location");

        foreach (var car in cars.OrderBy(c => c.Position))
        {
            sb.AppendLine($"{car.Position}," +
                         $"{car.RaceNumber}," +
                         $"{EscapeCsv(car.TeamName)}," +
                         $"{EscapeCsv(car.Driver)}," +
                         $"{EscapeCsv(car.CarModelName)}," +
                         $"{car.Laps}," +
                         $"{(car.GapSeconds.HasValue ? car.GapSeconds.Value.ToString("F3", CultureInfo.InvariantCulture) : "")}," +
                         $"{car.LastLapMs}," +
                         $"{car.BestLapMs}," +
                         $"{car.Kmh:F1}," +
                         $"{car.Location}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private async Task ExportStintsCsv(List<StintDto> stints, StintDto? currentStint, string filePath)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Stint ID,Driver,Driver Index,Lap Number,Started At (UTC),Ended At (UTC)," +
                     "Start Session Time,End Session Time,Duration (s),Notes,Planned Fuel (L),Planned Laps,Is Open");

        var allStints = new List<StintDto>(stints);
        if (currentStint != null && !allStints.Any(s => s.Id == currentStint.Id))
        {
            allStints.Add(currentStint);
        }

        foreach (var stint in allStints.OrderBy(s => s.StartedAt))
        {
            var duration = (stint.EndedAt ?? DateTime.UtcNow) - stint.StartedAt;
            var isOpen = stint.EndedAt == null;

            sb.AppendLine($"{stint.Id}," +
                         $"{EscapeCsv(stint.Driver)}," +
                         $"{stint.DriverIndex}," +
                         $"{stint.LapNumber}," +
                         $"{stint.StartedAt:yyyy-MM-dd HH:mm:ss}," +
                         $"{(stint.EndedAt.HasValue ? stint.EndedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "")}," +
                         $"{stint.StartSessionTime}," +
                         $"{(stint.EndSessionTime.HasValue ? stint.EndSessionTime.Value.ToString() : "")}," +
                         $"{duration.TotalSeconds:F1}," +
                         $"{EscapeCsv(stint.Notes ?? "")}," +
                         $"{(stint.PlannedFuelLiters.HasValue ? stint.PlannedFuelLiters.Value.ToString("F2", CultureInfo.InvariantCulture) : "")}," +
                         $"{stint.PlannedLaps}," +
                         $"{isOpen}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private async Task ExportLapTimesCsv(List<CarStateDto> cars, string filePath)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Race Number,Driver,Last Lap (ms),Best Lap (ms),Delta (ms)");

        foreach (var car in cars.OrderBy(c => c.Position))
        {
            sb.AppendLine($"{car.RaceNumber}," +
                         $"{EscapeCsv(car.Driver)}," +
                         $"{car.LastLapMs}," +
                         $"{car.BestLapMs}," +
                         $"{car.DeltaMs}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EnduranceHub.Shared.Dto;

namespace EnduranceHub.Client.Services;

public sealed class TelemetryGraphService
{
    public class LapData
    {
        public int LapNumber { get; set; }
        public int LapTimeMs { get; set; }
    }

    public class DriverGraphData
    {
        public string DriverName { get; set; } = "";
        public List<LapData> Laps { get; set; } = new();
        public int BestLapMs { get; set; }
        public int AverageLapMs { get; set; }
        public int TotalLaps { get; set; }
    }

    public (DriverGraphData Driver1, DriverGraphData Driver2) GetGraphData(
        SessionSnapshotDto snapshot,
        int driverIndex1,
        int driverIndex2)
    {
        var driver1Data = GetDriverData(snapshot, driverIndex1);
        var driver2Data = GetDriverData(snapshot, driverIndex2);

        return (driver1Data, driver2Data);
    }

    private DriverGraphData GetDriverData(SessionSnapshotDto snapshot, int driverIndex)
    {
        var stints = snapshot.Stints.Where(s => s.DriverIndex == driverIndex).ToList();
        var driverName = stints.FirstOrDefault()?.Driver ?? $"Driver {driverIndex}";

        // В реальном приложении здесь нужно получать данные о временах круга из БД или API
        // Сейчас создаем примерные данные на основе стинтов
        var laps = new List<LapData>();
        var lapCounter = 1;

        foreach (var stint in stints.OrderBy(s => s.StartedAt))
        {
            // Примерно рассчитываем количество кругов в стинте
            var stintDuration = (stint.EndedAt ?? DateTime.UtcNow) - stint.StartedAt;
            var estimatedLaps = (int)(stintDuration.TotalSeconds / 90); // Примерно 90 секунд на круг

            // Генерируем примерные времена кругов с небольшой вариацией
            var baseTime = 85000 + new Random(driverIndex * 1000 + stint.Id).Next(-5000, 5000);

            for (int i = 0; i < estimatedLaps; i++)
            {
                var variation = new Random(lapCounter * driverIndex).Next(-2000, 2000);
                laps.Add(new LapData
                {
                    LapNumber = lapCounter++,
                    LapTimeMs = baseTime + variation
                });
            }
        }

        var bestLap = laps.Count > 0 ? laps.Min(l => l.LapTimeMs) : 0;
        var avgLap = laps.Count > 0 ? (int)laps.Average(l => l.LapTimeMs) : 0;

        return new DriverGraphData
        {
            DriverName = driverName,
            Laps = laps,
            BestLapMs = bestLap,
            AverageLapMs = avgLap,
            TotalLaps = laps.Count
        };
    }
}

using AccEndurance.Broadcaster;
using AccEndurance.Shared.Broadcasting;

var mockMode = args.Contains("--mock");
var index = Array.IndexOf(args, "--mock");
var others = index >= 0 ? args.Where((_, i) => i != index).ToArray() : args;

var ip = others.Length > 0 ? others[0] : "127.0.0.1";
var port = others.Length > 1 ? int.Parse(others[1]) : 9000;
var displayName = others.Length > 2 ? others[2] : "AccBroadcaster";

Console.WriteLine($"Connecting to ACC at {ip}:{port} as '{displayName}'...");
Console.WriteLine("Waiting for broadcast packets. Press Ctrl+C to exit.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var mock = mockMode ? new MockAccServer(port) : null;
if (mock != null)
{
    Console.WriteLine("[MOCK] Local simulator started on same port.");
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            mock.ProcessPendingRequests(msg => Console.WriteLine(msg));
            mock.SendRealtimeCarUpdateAsync().Wait(cts.Token);
            await Task.Delay(1000, cts.Token);
        }
    });
}

using var protocol = new BroadcastingProtocol(ip, port, displayName);

protocol.OnConnectionStateChanged += msg => Console.WriteLine($"[Connection] {msg}");

protocol.OnEntrylistUpdate += car =>
{
    Console.WriteLine($"[ENTRY_LIST_CAR] #{car.RaceNumber} ({car.TeamName})");
    Console.WriteLine($"  ModelType={car.CarModelType} CupCategory={car.CupCategory} Drivers={car.Drivers.Count}");
    foreach (var d in car.Drivers)
        Console.WriteLine($"    {d}");
};

protocol.OnRealtimeUpdate += update =>
{
    Console.WriteLine($"[REALTIME_UPDATE] Session={update.SessionType} Phase={update.Phase}");
    Console.WriteLine($"  Time={update.SessionTime:mm\\:ss\\.fff}/{update.SessionEndTime:mm\\:ss\\.fff}");
    Console.WriteLine($"  Weather: Ambient={update.AmbientTemp}C Track={update.TrackTemp}C Rain={update.RainLevel:F1}/10 Clouds={update.Clouds:F1}/10");
};

protocol.OnRealtimeCarUpdate += update =>
{
    Console.WriteLine($"[REALTIME_CAR] Car={update.CarIndex} Driver={update.DriverIndex} Pos={update.Position}");
    Console.WriteLine($"  Speed={update.Kmh}km/h Gear={update.Gear} Laps={update.Laps} Spline={update.SplinePosition:F3} Delta={update.Delta}ms");
    Console.WriteLine($"  Location={update.CarLocation} LapTime={update.CurrentLap?.Laptime?.ToString(@"mm\:ss\.fff") ?? "N/A"}");
};

protocol.OnBroadcastingEvent += evt =>
{
    Console.WriteLine($"[EVENT] {evt.Type}: {evt.Msg} (time={evt.TimeMs}ms car={evt.CarId})");
};

protocol.OnTrackDataUpdate += track =>
{
    Console.WriteLine($"[TRACK] {track.TrackName} (id={track.TrackId}, {track.TrackMeters}m)");
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (TaskCanceledException) { }

Console.WriteLine("Shutting down...");
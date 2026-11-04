using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AccEndurance.Broadcaster;

using AccEndurance.Shared;
using AccEndurance.Shared.Broadcasting;

public sealed class MockAccServer : IDisposable
{
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<byte[]> _inbound = new();
    private IPEndPoint? _target;
    private int _connectionId = 100;
    private readonly List<CarInfo> _cars;
    private readonly Stopwatch _driverSwapTimer = Stopwatch.StartNew();
    private readonly double _driverSwapIntervalSeconds = 5.0;

    private readonly List<(float X, float Y)> _worldOutline = new();
    private double _phase;
    private int _lap;

    public MockAccServer(int port = 9000, List<CarInfo>? cars = null, double driverSwapIntervalSeconds = 5.0)
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        _cars = cars ?? DefaultGrid();
        _driverSwapIntervalSeconds = driverSwapIntervalSeconds;
        BuildWorldOutline();
        _ = ListenerLoopAsync(_cts.Token);
    }

    private void BuildWorldOutline()
    {
        _lap = 3;
        var track = Environment.GetEnvironmentVariable("ACC_TRACK") ?? "monza";
        var meters = TrackMetersFor(track);
        var outline = TrackLayouts.GetOutline(track, meters);
        // условные «мировые» метры, центр трассы в 0; пропорции сохраняем
        var spanX = meters * 0.5f;
        var spanY = meters * 0.30f;
        foreach (var p in outline)
            _worldOutline.Add((((float)p.X - 0.5f) * spanX, ((float)p.Y - 0.5f) * spanY));
    }

    private (float X, float Y) WorldAt(double phase)
    {
        if (_worldOutline.Count == 0) return (0f, 0f);
        var s = Math.Clamp(phase, 0.0, 0.999999);
        var n = _worldOutline.Count;
        var raw = s * n;
        var i0 = (int)raw;
        var f = raw - i0;
        var a = _worldOutline[i0 % n];
        var b = _worldOutline[(i0 + 1) % n];
        return ((float)(a.X + f * (b.X - a.X)), (float)(a.Y + f * (b.Y - a.Y)));
    }

    private static List<CarInfo> DefaultGrid()
    {
        return new List<CarInfo>
        {
            new()
            {
                CarIndex = 0,
                CarModelType = 32,
                TeamName = "Team OWL Racing",
                RaceNumber = 4,
                CupCategory = 0,
                CurrentDriverIndex = 0,
                Drivers =
                {
                    new DriverInfo { FirstName = "Иван", LastName = "Петров", ShortName = "IVA", Category = 0 },
                    new DriverInfo { FirstName = "Алексей", LastName = "Смирно́в", ShortName = "ALE", Category = 1 },
                    new DriverInfo { FirstName = "Михаил", LastName = "Кузнецов", ShortName = "MIK", Category = 1 },
                },
            },
            new()
            {
                CarIndex = 1,
                CarModelType = 30,
                TeamName = "Белая Молния",
                RaceNumber = 17,
                CupCategory = 1,
                CurrentDriverIndex = 0,
                Drivers =
                {
                    new DriverInfo { FirstName = "Жан", LastName = "Дюпон", ShortName = "DUP", Category = 2 },
                },
            },
        };
    }

    public async Task SendRealtimeCarUpdateAsync(CancellationToken ct = default)
    {
        SendRealtimeUpdate();

        var burst = new List<(float Spline, float X, float Y, ushort Laps)>(8);
        CarInfo? ourCar = null;
        foreach (var car in _cars)
        {
            if (_driverSwapTimer.Elapsed.TotalSeconds >= _driverSwapIntervalSeconds)
            {
                if (car.Drivers.Count >= 3)
                {
                    car.CurrentDriverIndex = (byte)((car.CurrentDriverIndex + 1) % car.Drivers.Count);
                    _driverSwapTimer.Restart();
                }
            }

            if (car.Drivers.Count < 3)
            {
                Send(CarUpdatePacket(car, 0.25f + 0.01f * car.CarIndex, 3, 123.45f, -67.89f));
                await Task.Delay(50, ct);
                continue;
            }

            // наш автомобиль «едет» по контуру: серия точек за один проход,
            // чтобы трасса записывалась как плотный многоугольник
            ourCar = car;
            const int samplesPerPass = 8;
            var step = 0.00625;
            for (int k = 0; k < samplesPerPass; k++)
            {
                _phase += step;
                if (_phase >= 1.0)
                {
                    _phase -= 1.0;
                    _lap++;
                }
                var (wx, wy) = WorldAt(_phase);
                burst.Add(((float)_phase, wx, wy, (ushort)_lap));
            }
        }

        if (ourCar == null) return;
        foreach (var (spline, wx, wy, laps) in burst)
        {
            Send(CarUpdatePacket(ourCar, spline, laps, wx, wy));
            await Task.Delay(3, ct);
        }
    }

    private byte[] CarUpdatePacket(CarInfo car, float spline, ushort laps, float wx, float wy)
    {
        var update = new RealtimeCarUpdate
        {
            CarIndex = car.CarIndex,
            DriverIndex = car.CurrentDriverIndex,
            DriverCount = (byte)car.Drivers.Count,
            Gear = 4,
            WorldPosX = wx,
            WorldPosY = wy,
            Yaw = 1.2f,
            CarLocation = CarLocation.Track,
            Kmh = 180,
            Position = (ushort)(car.CarIndex + 1),
            CupPosition = (ushort)(car.CarIndex + 1),
            TrackPosition = (ushort)(car.CarIndex + 1),
            SplinePosition = spline,
            Laps = laps,
            Delta = -1200,
        };
        return EncodeRealtimeCarUpdate(update);
    }

    private byte[] EncodeRealtimeCarUpdate(RealtimeCarUpdate u)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)InboundMessageTypes.RealtimeCarUpdate);
        w.Write(u.CarIndex);
        w.Write(u.DriverIndex);
        w.Write(u.DriverCount);
        w.Write((byte)(u.Gear + 2));
        w.Write(u.WorldPosX);
        w.Write(u.WorldPosY);
        w.Write(u.Yaw);
        w.Write((byte)u.CarLocation);
        w.Write(u.Kmh);
        w.Write(u.Position);
        w.Write(u.CupPosition);
        w.Write(u.TrackPosition);
        w.Write(u.SplinePosition);
        w.Write(u.Laps);
        w.Write(u.Delta);
        WriteLap(w, u.BestSessionLap);
        WriteLap(w, u.LastLap);
        WriteLap(w, u.CurrentLap);
        return ms.ToArray();
    }

    private void SendRealtimeUpdate()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)InboundMessageTypes.RealtimeUpdate);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((byte)SessionType.Race);
        w.Write((byte)SessionPhase.Session);
        w.Write(1_000_000f);
        w.Write(10_000_000f);
        w.Write(-1);
        WriteString(w, "TV");
        WriteString(w, "TV_Far");
        WriteString(w, "Tacho");
        w.Write((byte)0);
        w.Write(14.0f * 3600 * 1000);
        w.Write((byte)23);
        w.Write((byte)26);
        w.Write((byte)15);
        w.Write((byte)2);
        w.Write((byte)4);
        WriteLap(w, new LapInfo { LaptimeMs = 1_42_000, Splits = new[] { (int?)20_000, 60_000, 100_000 }, IsValidForBest = true });
        Send(ms.ToArray());
    }

    private static void WriteLap(BinaryWriter w, LapInfo? lap)
    {
        lap ??= new LapInfo { LaptimeMs = int.MaxValue, Splits = new[] { (int?)null, null, null } };
        w.Write(lap.LaptimeMs);
        w.Write(lap.CarIndex);
        w.Write(lap.DriverIndex);
        w.Write((byte)lap.Splits.Count);
        foreach (var s in lap.Splits) w.Write(s ?? int.MaxValue);
        w.Write(lap.IsInvalid ? (byte)1 : (byte)0);
        w.Write(lap.IsValidForBest ? (byte)1 : (byte)0);
        w.Write(lap.Type == "Outlap" ? (byte)1 : (byte)0);
        w.Write(lap.Type == "Inlap" ? (byte)1 : (byte)0);
    }

    private async Task ListenerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _client.ReceiveAsync(ct);
                _target = result.RemoteEndPoint;
                _inbound.Enqueue(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public void ProcessPendingRequests(Action<string>? log = null)
    {
        while (_inbound.TryDequeue(out var data))
        {
            if (data.Length < 1) continue;
            var type = (OutboundMessageTypes)data[0];
            switch (type)
            {
                case OutboundMessageTypes.RegisterCommandApplication:
                    _connectionId++;
                    SendRegistrationResult();
                    log?.Invoke($"[mock] REGISTER received");
                    break;
                case OutboundMessageTypes.RequestEntryList:
                    SendEntryList();
                    log?.Invoke($"[mock] ENTRY_LIST requested");
                    break;
                case OutboundMessageTypes.RequestTrackData:
                    SendTrackData();
                    log?.Invoke($"[mock] TRACK_DATA requested");
                    break;
                case OutboundMessageTypes.UnregisterCommandApplication:
                    log?.Invoke($"[mock] UNREGISTER received");
                    break;
            }
        }
    }

    private void SendRegistrationResult()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)InboundMessageTypes.RegistrationResult);
        w.Write(_connectionId);
        w.Write((byte)1);
        w.Write((byte)0);
        WriteString(w, "");
        Send(ms.ToArray());
    }

    private void SendEntryList()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)InboundMessageTypes.EntryList);
        w.Write(_connectionId);
        var cars = _cars.Where(c => c.CarIndex < 40).ToList();
        w.Write((ushort)cars.Count);
        foreach (var car in cars) w.Write(car.CarIndex);
        Send(ms.ToArray());

        foreach (var car in cars) SendEntryListCar(car);
    }

    private void SendEntryListCar(CarInfo car)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)InboundMessageTypes.EntryListCar);
        w.Write(car.CarIndex);
        w.Write(car.CarModelType);
        WriteString(w, car.TeamName);
        w.Write(car.RaceNumber);
        w.Write(car.CupCategory);
        w.Write(car.CurrentDriverIndex);
        w.Write(car.Nationality);
        w.Write((byte)car.Drivers.Count);
        foreach (var d in car.Drivers)
        {
            WriteString(w, d.FirstName);
            WriteString(w, d.LastName);
            WriteString(w, d.ShortName);
            w.Write(d.Category);
            w.Write(d.Nationality);
        }
        Send(ms.ToArray());
    }

    private void SendTrackData()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)InboundMessageTypes.TrackData);
        w.Write(_connectionId);
        var track = Environment.GetEnvironmentVariable("ACC_TRACK") ?? "monza";
        WriteString(w, track);
        w.Write(1);
        w.Write(TrackMetersFor(track));
        w.Write((byte)0);
        w.Write((byte)0);
        Send(ms.ToArray());
    }

    private static int TrackMetersFor(string track)
    {
        var key = track.Trim().ToLowerInvariant();
        if (key.Contains("nurburgring") || key.Contains("24h")) return 20832;
        return key switch
        {
            "monza" => 5793,
            "spa" => 7004,
            "zandvoort" => 4259,
            "barcelona" => 4655,
            "silverstone" => 5901,
            _ => 5000,
        };
    }

    private void Send(byte[] packet)
    {
        var target = _target;
        if (target == null)
        {
            Console.Error.WriteLine("[mock] no target endpoint yet");
            return;
        }
        _client.Send(packet, packet.Length, target);
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _client.Dispose();
    }
}
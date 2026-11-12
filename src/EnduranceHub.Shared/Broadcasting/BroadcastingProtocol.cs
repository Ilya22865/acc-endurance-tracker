using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace EnduranceHub.Shared.Broadcasting;

public sealed class BroadcastingProtocol : IDisposable
{
    public const int ProtocolVersion = 4;
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private readonly string _displayName;
    private readonly string _connectionPassword;
    private readonly int _msInterval;
    private readonly string _commandPassword;

    private static readonly TimeSpan LostTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RegisterRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EntryListRetryInterval = TimeSpan.FromSeconds(5);

    private volatile bool _registered;
    private DateTime _lastEntryListRequestUtc = DateTime.MinValue;
    private DateTime _lastMessageUtc = DateTime.UtcNow;
    private DateTime _lastRegisterSentUtc = DateTime.MinValue;
    // Unregister имеет смысл только если мы уже регистрировались на этом сокете.
    private bool _hasRegistered;

    public int ConnectionId { get; private set; }
    public string ConnectionIdentifier { get; }

    private readonly Dictionary<ushort, CarInfo> _entryList = new();

    public event Action<string>? OnConnectionStateChanged;
    public event Action<CarInfo>? OnEntrylistUpdate;
    /// <summary>Полный (авторитетный) набор индексов машин из сообщения EntryList.</summary>
    public event Action<IReadOnlyList<ushort>>? OnEntryListIndices;
    public event Action<RealtimeUpdate>? OnRealtimeUpdate;
    public event Action<RealtimeCarUpdate>? OnRealtimeCarUpdate;
    public event Action<BroadcastingEvent>? OnBroadcastingEvent;
    public event Action<TrackData>? OnTrackDataUpdate;

    public IEnumerable<CarInfo> EntryList => _entryList.Values;

    public BroadcastingProtocol(string ip, int port, string displayName,
        string connectionPassword = "", string commandPassword = "", int msRealtimeUpdateInterval = 100)
    {
        ConnectionIdentifier = $"{ip}:{port}";
        _displayName = displayName;
        _connectionPassword = connectionPassword;
        _commandPassword = commandPassword;
        _msInterval = msRealtimeUpdateInterval;
        _client = new UdpClient();
        _client.Connect(ip, port);
    }

    private int _started;

    /// <summary>
    /// Запускает приём и регистрацию. Вызывать ПОСЛЕ подписки на события: раньше циклы стартовали
    /// прямо в конструкторе, и первые ответы ACC (в т.ч. список машин) могли прийти до подписки и потеряться.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = ListenerLoopAsync(_cts.Token);
        _ = RegistrationLoopAsync(_cts.Token);
    }

    private async Task RegistrationLoopAsync(CancellationToken ct)
    {
        // ACC не прощает одноразовую регистрацию: если игра запустилась позже
        // сервера или перезапустилась, нужно заново послать Register.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_registered)
                {
                    if (DateTime.UtcNow - _lastRegisterSentUtc >= RegisterRetryInterval)
                    {
                        // ВАЖНО: сначала снимаем прошлую регистрацию с этого же сокета.
                        // ACC создаёт новое соединение на каждый Register, а Unregister
                        // не шлёт никто — соединения копятся. Каждое из них получает
                        // копию каждого события (в логе видно 2, 5, 15 одинаковых
                        // LapCompleted подряд), а когда их становится слишком много,
                        // игра перестаёт присылать realtime-обновления вовсе.
                        if (_hasRegistered)
                            Unregister();
                        RequestConnection(_displayName, _connectionPassword, _msInterval, _commandPassword);
                        _lastRegisterSentUtc = DateTime.UtcNow;
                    }
                }
                else if (DateTime.UtcNow - _lastMessageUtc > LostTimeout)
                {
                    // от игры давно ничего не шло — считаем связь потерянной
                    // (и сообщаем об этом: раньше статус «подключено» оставался навсегда).
                    // Перерегистрируемся на следующей итерации, но не чаще RegisterRetryInterval.
                    _registered = false;
                    _lastRegisterSentUtc = DateTime.MinValue;
                    OnConnectionStateChanged?.Invoke($"Lost: no messages for {LostTimeout.TotalSeconds:F0}s");
                }
                else
                {
                    RequestEntryListIfIncomplete();
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Периодически перезапрашивает список машин, пока он неполный. UDP не гарантирует
    /// доставку, а без списка в таблице позиций вместо номеров и команд остаются нули.
    /// </summary>
    private void RequestEntryListIfIncomplete()
    {
        if (DateTime.UtcNow - _lastEntryListRequestUtc < EntryListRetryInterval) return;

        bool incomplete;
        lock (_lock)
        {
            incomplete = _entryList.Count == 0 || _entryList.Values.Any(c => c.Drivers.Count == 0);
        }
        if (!incomplete) return;

        _lastEntryListRequestUtc = DateTime.UtcNow;
        SendRequest(RequestType.EntryList);
    }

    private async Task ListenerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _client.ReceiveAsync(ct);
                await using var ms = new MemoryStream(result.Buffer, false);
                using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
                ProcessMessage(reader);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // UDP «connection reset» приходит, пока ACC не запущен; без паузы цикл крутится вхолостую
                Console.Error.WriteLine($"Socket error: {ex.Message}");
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Parse error: {ex}");
            }
        }
    }

    private void ProcessMessage(BinaryReader reader)
    {
        _lastMessageUtc = DateTime.UtcNow;
        var messageType = (InboundMessageTypes)reader.ReadByte();
        switch (messageType)
        {
            case InboundMessageTypes.RegistrationResult:
            {
                ConnectionId = reader.ReadInt32();
                var success = reader.ReadByte() > 0;
                var isReadonly = reader.ReadByte() == 0;
                var error = ReadString(reader);
                OnConnectionStateChanged?.Invoke($"{(success ? "Connected" : "Failed")}, readonly={isReadonly}, error={error}");
                if (success)
                {
                    // Сначала обновляем время/флаги, потом _registered: иначе цикл
                    // регистрации успевает увидеть «зарегистрированы» со старым
                    // _lastEntryListRequestUtc и шлёт лишний запрос списка машин.
                    _hasRegistered = true;
                    _lastMessageUtc = DateTime.UtcNow;
                    _lastEntryListRequestUtc = DateTime.UtcNow;
                    _registered = true;
                    SendRequest(RequestType.EntryList);
                    SendRequest(RequestType.TrackData);
                }
                else
                {
                    _registered = false;
                }
                break;
            }
            case InboundMessageTypes.EntryList:
            {
                _ = reader.ReadInt32();
                var carCount = reader.ReadUInt16();
                var listedCars = new List<ushort>(carCount);
                for (int i = 0; i < carCount; i++)
                {
                    var carIndex = reader.ReadUInt16();
                    listedCars.Add(carIndex);
                    // Не затираем уже полученные данные (EntryListCar может прийти раньше
                    // или повторно): иначе в онлайне пропадают названия команд.
                    if (!_entryList.ContainsKey(carIndex))
                        _entryList[carIndex] = new CarInfo { CarIndex = carIndex };
                }

                // Список машин — авторитетный: убираем только те индексы, которых в нём нет.
                // Пустой список часто приходит до загрузки сессии — не стираем известные машины.
                if (listedCars.Count > 0)
                {
                    var listed = listedCars.ToHashSet();
                    foreach (var stale in _entryList.Keys.Where(k => !listed.Contains(k)).ToList())
                        _entryList.Remove(stale);
                }

                OnEntryListIndices?.Invoke(listedCars);
                break;
            }
            case InboundMessageTypes.EntryListCar:
            {
                var carIndex = reader.ReadUInt16();
                // UDP не гарантирует порядок: EntryListCar может прийти до EntryList.
                // Раньше такая машина отбрасывалась, и команда оставалась без названия.
                if (!_entryList.TryGetValue(carIndex, out var car))
                {
                    car = new CarInfo { CarIndex = carIndex };
                    _entryList[carIndex] = car;
                }

                car.CarModelType = reader.ReadByte();
                car.TeamName = ReadString(reader);
                car.RaceNumber = reader.ReadInt32();
                car.CupCategory = reader.ReadByte();
                car.CurrentDriverIndex = reader.ReadByte();
                car.Nationality = reader.ReadUInt16();

                car.Drivers.Clear();
                var driverCount = reader.ReadByte();
                for (int i = 0; i < driverCount; i++)
                {
                    car.Drivers.Add(new DriverInfo
                    {
                        FirstName = ReadString(reader),
                        LastName = ReadString(reader),
                        ShortName = ReadString(reader),
                        Category = reader.ReadByte(),
                        Nationality = reader.ReadUInt16(),
                    });
                }

                OnEntrylistUpdate?.Invoke(car);
                break;
            }
            case InboundMessageTypes.RealtimeUpdate:
            {
                var update = new RealtimeUpdate
                {
                    EventIndex = reader.ReadUInt16(),
                    SessionIndex = reader.ReadUInt16(),
                    SessionType = (SessionType)reader.ReadByte(),
                    Phase = (SessionPhase)reader.ReadByte(),
                    SessionTime = TimeSpan.FromMilliseconds(reader.ReadSingle()),
                    SessionEndTime = TimeSpan.FromMilliseconds(reader.ReadSingle()),
                    FocusedCarIndex = reader.ReadInt32(),
                    ActiveCameraSet = ReadString(reader),
                    ActiveCamera = ReadString(reader),
                    CurrentHudPage = ReadString(reader),
                };

                var isReplay = reader.ReadByte() > 0;
                if (isReplay)
                {
                    _ = reader.ReadSingle();
                    _ = reader.ReadSingle();
                }

                update.TimeOfDay = TimeSpan.FromMilliseconds(reader.ReadSingle());
                update.AmbientTemp = reader.ReadByte();
                update.TrackTemp = reader.ReadByte();
                update.Clouds = reader.ReadByte() / 10.0f;
                update.RainLevel = reader.ReadByte() / 10.0f;
                update.Wetness = reader.ReadByte() / 10.0f;
                update.BestSessionLap = ReadLap(reader);

                OnRealtimeUpdate?.Invoke(update);
                break;
            }
            case InboundMessageTypes.RealtimeCarUpdate:
            {
                var update = new RealtimeCarUpdate
                {
                    CarIndex = reader.ReadUInt16(),
                    DriverIndex = reader.ReadUInt16(),
                    DriverCount = reader.ReadByte(),
                    Gear = (short)(reader.ReadByte() - 2),
                    WorldPosX = reader.ReadSingle(),
                    WorldPosY = reader.ReadSingle(),
                    Yaw = reader.ReadSingle(),
                    CarLocation = (CarLocation)reader.ReadByte(),
                    Kmh = reader.ReadUInt16(),
                    Position = reader.ReadUInt16(),
                    CupPosition = reader.ReadUInt16(),
                    TrackPosition = reader.ReadUInt16(),
                    SplinePosition = reader.ReadSingle(),
                    Laps = reader.ReadUInt16(),
                    Delta = reader.ReadInt32(),
                    BestSessionLap = ReadLap(reader),
                    LastLap = ReadLap(reader),
                    CurrentLap = ReadLap(reader),
                };

                // Как в официальном SDK: если машины нет в списке или число пилотов изменилось
                // (пакеты UDP терялись / смена пилота), запрашиваем список заново (не чаще раза в секунду).
                // До успешной регистрации ConnectionId ещё 0 — такой запрос игра всё равно проигнорирует.
                if (_registered
                    && (!_entryList.TryGetValue(update.CarIndex, out var knownCar) || knownCar.Drivers.Count != update.DriverCount)
                    && DateTime.UtcNow - _lastEntryListRequestUtc > TimeSpan.FromSeconds(1))
                {
                    _lastEntryListRequestUtc = DateTime.UtcNow;
                    SendRequest(RequestType.EntryList);
                }

                OnRealtimeCarUpdate?.Invoke(update);
                break;
            }
            case InboundMessageTypes.TrackData:
            {
                _ = reader.ReadInt32();
                var track = new TrackData
                {
                    TrackName = ReadString(reader),
                    TrackId = reader.ReadInt32(),
                    TrackMeters = reader.ReadInt32(),
                };
                OnTrackDataUpdate?.Invoke(track);
                break;
            }
            case InboundMessageTypes.BroadcastingEvent:
            {
                var evt = new BroadcastingEvent
                {
                    Type = (BroadcastingCarEvent)reader.ReadByte(),
                    Msg = ReadString(reader),
                    TimeMs = reader.ReadInt32(),
                    CarId = reader.ReadInt32(),
                };
                _entryList.TryGetValue((ushort)evt.CarId, out var car);
                evt.CarData = car;
                OnBroadcastingEvent?.Invoke(evt);
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown message type {messageType}");
                break;
        }
    }

    private static LapInfo? ReadLap(BinaryReader reader)
    {
        var lap = new LapInfo
        {
            LaptimeMs = reader.ReadInt32(),
            CarIndex = reader.ReadUInt16(),
            DriverIndex = reader.ReadUInt16(),
        };

        var splitCount = reader.ReadByte();
        var splits = new List<int?>(splitCount);
        for (int i = 0; i < splitCount; i++)
        {
            var split = reader.ReadInt32();
            splits.Add(split == int.MaxValue ? null : split);
        }
        while (splits.Count < 3) splits.Add(null);

        lap.Splits = splits;
        lap.IsInvalid = reader.ReadByte() > 0;
        lap.IsValidForBest = reader.ReadByte() > 0;

        var isOutlap = reader.ReadByte() > 0;
        var isInlap = reader.ReadByte() > 0;
        lap.Type = isOutlap ? "Outlap" : isInlap ? "Inlap" : "Regular";

        return lap.LaptimeMs == int.MaxValue
            && lap.Splits.All(s => s is null)
            ? null
            : lap;
    }

    /// <summary>
    /// Сообщает ACC, что это соединение больше не нужно. Без этого каждое повторное
    /// Register создаёт в игре новое «фантомное» соединение.
    /// </summary>
    private void Unregister()
    {
        lock (_lock)
        {
            try
            {
                byte[] payload = Build(bytes => bytes.Write((byte)OutboundMessageTypes.UnregisterCommandApplication));
                _client.Send(payload, payload.Length);
            }
            catch
            {
                // сокет уже закрыт — нечего снимать
            }
        }
    }

    private void RequestConnection(string displayName, string connectionPassword, int msInterval, string commandPassword)
    {
        lock (_lock)
        {
            byte[] payload = Build(bytes =>
            {
                bytes.Write((byte)OutboundMessageTypes.RegisterCommandApplication);
                bytes.Write((byte)ProtocolVersion);
                WriteString(bytes, displayName);
                WriteString(bytes, connectionPassword);
                bytes.Write(msInterval);
                WriteString(bytes, commandPassword);
            });
            _client.Send(payload, payload.Length);
        }
    }

    private enum RequestType
    {
        EntryList,
        TrackData,
    }

    private void SendRequest(RequestType type)
    {
        lock (_lock)
        {
            byte[] payload = Build(bytes =>
            {
                bytes.Write((byte)(type == RequestType.EntryList
                    ? OutboundMessageTypes.RequestEntryList
                    : OutboundMessageTypes.RequestTrackData));
                bytes.Write(ConnectionId);
            });
            _client.Send(payload, payload.Length);
        }
    }

    private static byte[] Build(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        write(writer);
        return ms.ToArray();
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        if (length == 0) return "";
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteString(BinaryWriter writer, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    public void Dispose()
    {
        Unregister();
        _cts.Cancel();
        _client.Dispose();
    }
}
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace EnduranceHub.Agent;

[SupportedOSPlatform("windows")]
public sealed class AccSharedMemoryReader : IAccDataSource, IDisposable
{
    private MemoryMappedFile? _physics;
    private MemoryMappedViewAccessor? _view;
    private MemoryMappedFile? _graphics;
    private MemoryMappedViewAccessor? _graphicsView;
    private readonly object _lock = new();

    private const int PacketIdOffset = 0;
    private const int GasOffset = 4;
    private const int BrakeOffset = 8;
    private const int FuelOffset = 12;
    private const int SteerAngleOffset = 24;
    // SPageFilePhysics, pack(4): wheelLoad[4] с 72, wheelsPressure[4] с 88,
    // tyreCoreTemperature[4] с 152. Смещения совпадают с gas/brake/steerAngle выше.
    private const int TyrePressureOffset = 88;
    private const int TyreCoreTempOffset = 152;
    private const int TyreMinCapacity = TyreCoreTempOffset + 4 * 4;

    // Смещения в SPageFileGraphic (ACC, pack(4)): status — 4, flag игрока — 1224, глобальные флаги — с 1548.
    private const int GStatusOffset = 4;
    private const int GPlayerFlagOffset = 1224;
    private const int GGlobalYellowOffset = 1548;   // yellow, yellow1, yellow2, yellow3, white, green, chequered, red
    private const int GMinCapacity = GGlobalYellowOffset + 8 * 4;

    public event Action<TelemetryReading>? OnSample;

    public void Start()
    {
        AttachPhysics();
    }

    private void AttachPhysics()
    {
        try
        {
            _view?.Dispose();
            _physics?.Dispose();
            _physics = MemoryMappedFile.OpenExisting(@"Local\acpmf_physics");
            _view = _physics.CreateViewAccessor();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ACC ещё не запущен, закрылся или работает под другим пользователем.
            // Раньше необработанное исключение здесь могло уронить весь Agent.
            // FileNotFoundException — обычное состояние «ACC не запущен», не засоряем лог.
            if (ex is not FileNotFoundException)
                Console.WriteLine($"[warn] physics shared memory: {ex.Message}");
            _physics = null;
            _view = null;
        }
    }

    private void AttachGraphics()
    {
        try
        {
            _graphicsView?.Dispose();
            _graphics?.Dispose();
            _graphics = MemoryMappedFile.OpenExisting(@"Local\acpmf_graphics");
            _graphicsView = _graphics.CreateViewAccessor();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (ex is not FileNotFoundException)
                Console.WriteLine($"[warn] graphics shared memory: {ex.Message}");
            _graphics = null;
            _graphicsView = null;
        }
    }

    /// <summary>Флаги читаются осторожно: если значения выглядят неправдоподобно (другая версия ACC), флаги не отдаём.</summary>
    public bool TryReadFlags(out FlagReading flags)
    {
        flags = default;
        var view = _graphicsView;
        if (view == null)
        {
            AttachGraphics();
            view = _graphicsView;
            if (view == null) return false;
        }

        lock (_lock)
        {
            try
            {
                if (view.Capacity < GMinCapacity) return false;

                var status = view.ReadInt32(GStatusOffset);
                var playerFlag = view.ReadInt32(GPlayerFlagOffset);
                if (status < 0 || status > 3) return false;
                if (playerFlag < 0 || playerFlag > 8) playerFlag = 0;

                var g = new bool[8];
                for (int i = 0; i < 8; i++)
                {
                    var v = view.ReadInt32(GGlobalYellowOffset + i * 4);
                    if (v != 0 && v != 1) return false;
                    g[i] = v == 1;
                }

                flags = new FlagReading(status, playerFlag, g[0], g[1], g[2], g[3], g[4], g[5], g[6], g[7]);
                return true;
            }
            catch (Exception ex) when (ex is AccessViolationException or ObjectDisposedException or ArgumentException or IOException)
            {
                _graphicsView?.Dispose();
                _graphics?.Dispose();
                _graphicsView = null;
                _graphics = null;
                return false;
            }
        }
    }

    public bool TryReadSample(out TelemetryReading sample)
    {
        sample = default;
        var view = _view;
        if (view == null)
        {
            // ACC ещё не запущен (или перезапущен): пробуем подключиться заново.
            // Раньше после успешного AttachPhysics строка «if (view == null) _view = null»
            // тут же сбрасывала свежесозданный доступ, и агент, запущенный до игры, никогда не читал данные.
            AttachPhysics();
            view = _view;
            if (view == null) return false;
        }

        lock (_lock)
        {
            try
            {
                var packetId = view.ReadInt32(PacketIdOffset);
                if (packetId == 0) return false;

                sample = new TelemetryReading(
                    packetId,
                    view.ReadSingle(GasOffset),
                    view.ReadSingle(BrakeOffset),
                    view.ReadSingle(SteerAngleOffset));
                return true;
            }
            catch (Exception ex) when (ex is AccessViolationException or ObjectDisposedException or ArgumentException or IOException)
            {
                // отображение устарело (ACC закрыт) — отпустим и подключимся заново при следующем чтении
                _view?.Dispose();
                _physics?.Dispose();
                _view = null;
                _physics = null;
                return false;
            }
        }
    }

    /// <summary>
    /// Давления/температуры шин и топливо. Читаются реже, чем сэмплы газа/тормоза
    /// (раз на отправляемую пачку), поэтому отдельный метод.
    /// </summary>
    public bool TryReadTyres(out TyreReading tyres)
    {
        tyres = default;
        var view = _view;
        if (view == null)
        {
            AttachPhysics();
            view = _view;
            if (view == null) return false;
        }

        lock (_lock)
        {
            try
            {
                if (view.Capacity < TyreMinCapacity) return false;

                var reading = new TyreReading(
                    view.ReadSingle(FuelOffset),
                    view.ReadSingle(TyrePressureOffset),
                    view.ReadSingle(TyrePressureOffset + 4),
                    view.ReadSingle(TyrePressureOffset + 8),
                    view.ReadSingle(TyrePressureOffset + 12),
                    view.ReadSingle(TyreCoreTempOffset),
                    view.ReadSingle(TyreCoreTempOffset + 4),
                    view.ReadSingle(TyreCoreTempOffset + 8),
                    view.ReadSingle(TyreCoreTempOffset + 12));

                if (!reading.IsValid) return false;
                tyres = reading;
                return true;
            }
            catch (Exception ex) when (ex is AccessViolationException or ObjectDisposedException or ArgumentException or IOException)
            {
                _view?.Dispose();
                _physics?.Dispose();
                _view = null;
                _physics = null;
                return false;
            }
        }
    }

    public void Dispose()
    {
        _graphicsView?.Dispose();
        _graphics?.Dispose();
        _graphicsView = null;
        _graphics = null;
        _view?.Dispose();
        _physics?.Dispose();
        _view = null;
        _physics = null;
    }
}
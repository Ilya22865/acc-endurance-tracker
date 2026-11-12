using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EnduranceHub.Shared.Net;

namespace EnduranceHub.Agent;

/// <summary>Результат отправки пачки на сервер.</summary>
public enum SendResult
{
    /// <summary>Сервер принял данные.</summary>
    Sent,

    /// <summary>Сервер ответил ошибкой (обычно 409 — потерял регистрацию после перезапуска).</summary>
    Rejected,

    /// <summary>Сеть недоступна/таймаут. Данные нужно сохранить и отправить позже.</summary>
    Unreachable,
}

public sealed class ServerClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(4);

    private readonly object _gate = new();
    private readonly object _logGate = new();
    private readonly Dictionary<string, DateTime> _lastLog = new();
    private readonly string _baseUrl;
    private readonly string _agentId;
    private HttpClient _http;
    private bool _disposed;
    private int _consecutiveFailures;

    public ServerClient(string baseUrl, string agentId)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _agentId = agentId;
        _http = DirectHttp.CreateClient(RequestTimeout);
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    // Включение/выключение VPN или смена сети делают старые keep-alive сокеты
    // мёртвыми. Сбрасываем пул, чтобы следующая попытка открыла соединение
    // по актуальному маршруту (и снова напрямую, без системного прокси).
    private void OnNetworkAddressChanged(object? sender, EventArgs e) => ResetConnection();

    public void ResetConnection()
    {
        HttpClient old;
        lock (_gate)
        {
            if (_disposed) return;
            old = _http;
            _http = DirectHttp.CreateClient(RequestTimeout);
            _consecutiveFailures = 0;
        }

        // Не рвём запрос, который уже выполняется: даём ему завершиться.
        _ = Task.Delay(TimeSpan.FromSeconds(5))
            .ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
    }

    private HttpClient Http
    {
        get { lock (_gate) return _http; }
    }

    public async Task<List<TeamDriverDto>> GetTeamDriversAsync(CancellationToken ct = default)
    {
        var raw = await Http.GetFromJsonAsync<List<TeamDriverDto>>($"{_baseUrl}/api/drivers/team", ct);
        return raw ?? new List<TeamDriverDto>();
    }

    public async Task<bool> RegisterAsync(int driverIndex, string driverName, CancellationToken ct = default)
    {
        var payload = new
        {
            AgentId = _agentId,
            DriverIndex = driverIndex,
            DriverName = driverName,
        };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(BatchTimeout);

            using var response = await Http.PostAsJsonAsync($"{_baseUrl}/api/telemetry/register", payload, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                _consecutiveFailures = 0;
                return true;
            }

            WarnThrottled("register-status",
                $"[warn] Регистрация: сервер ответил {(int)response.StatusCode} {response.StatusCode}");
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            WarnThrottled("register-timeout", $"[warn] Регистрация: сервер не ответил за {BatchTimeout.TotalSeconds:0} с");
            return false;
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            WarnThrottled("register-network", $"[warn] Регистрация недоступна ({_baseUrl}): {Describe(ex)}");
            return false;
        }
    }

    /// <summary>
    /// Отправляет пачку. При <see cref="SendResult.Unreachable"/> вызывающий обязан
    /// сохранить сэмплы: раньше пачка очищалась в любом случае, и при обрыве
    /// связи (VPN, Wi-Fi) телеметрия терялась безвозвратно.
    /// </summary>
    public async Task<SendResult> SendBatchAsync(
        IReadOnlyList<TelemetryReading> readings,
        FlagReading? flags,
        CancellationToken ct = default)
    {
        if (readings.Count == 0 && flags == null) return SendResult.Sent;

        // DriverId на сервере — это тот же AgentId, под которым агент зарегистрировался.
        // Раньше здесь уходило «AgentId:DriverIndex», сервер не находил регистрацию и отвечал 409 на каждую пачку —
        // поэтому телеметрия до клиента не доходила.
        var payload = new
        {
            DriverId = _agentId,
            Samples = readings.Select(r => new
            {
                r.PacketId,
                r.Gas,
                r.Brake,
                r.SteerAngle,
            }).ToList(),
            Flags = flags is FlagReading f
                ? new
                {
                    f.Status,
                    f.PlayerFlag,
                    f.Yellow,
                    f.Yellow1,
                    f.Yellow2,
                    f.Yellow3,
                    f.White,
                    f.Green,
                    f.Chequered,
                    f.Red,
                }
                : null,
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(BatchTimeout);

            using var response = await Http.PostAsJsonAsync($"{_baseUrl}/api/telemetry/batch", payload, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                _consecutiveFailures = 0;
                return SendResult.Sent;
            }

            WarnThrottled("batch-status",
                $"[warn] Сервер ответил {(int)response.StatusCode} {response.StatusCode} на отправку телеметрии");

            // 400/401/403/409 — сервер не принял данные из-за регистрации/запроса:
            // сразу перерегистрируемся. Остальные (5xx, 429) — временные: данные
            // не теряем, повторяем с backoff (иначе агент долбил бы регистрацию).
            return response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Conflict
                ? SendResult.Rejected
                : SendResult.Unreachable;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            MarkNetworkFailure();
            WarnThrottled("batch-timeout",
                $"[warn] Нет ответа от сервера за {BatchTimeout.TotalSeconds:0} с ({_baseUrl}). Телеметрия буферизуется.");
            return SendResult.Unreachable;
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            MarkNetworkFailure();
            WarnThrottled("batch-network",
                $"[warn] Сеть недоступна ({_baseUrl}): {Describe(ex)}. Телеметрия буферизуется.");
            return SendResult.Unreachable;
        }
    }

    private void MarkNetworkFailure()
    {
        // После нескольких подряд ошибок пересоздаём пул соединений: в нём могли
        // остаться сокеты, привязанные к старому маршруту/интерфейсу.
        if (Interlocked.Increment(ref _consecutiveFailures) >= 3)
            ResetConnection();
    }

    private static bool IsNetworkError(Exception ex)
        => ex is HttpRequestException or IOException or SocketException or ObjectDisposedException;

    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
        {
            parts.Add(e is SocketException se
                ? $"{e.GetType().Name} ({se.SocketErrorCode}): {e.Message}"
                : $"{e.GetType().Name}: {e.Message}");
        }
        return string.Join(" <- ", parts);
    }

    private void WarnThrottled(string key, string message)
    {
        var now = DateTime.UtcNow;
        lock (_logGate)
        {
            if (_lastLog.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(10))
                return;
            _lastLog[key] = now;
        }

        Console.WriteLine(message);
    }

    public void Dispose()
    {
        HttpClient http;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            http = _http;
        }

        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        http.Dispose();
    }

    public sealed class TeamDriverDto
    {
        public int ExtId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string Category { get; set; } = "";
        public int DriverIndex { get; set; }
        public bool IsActiveNow { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? $"{FirstName} {LastName} ({ShortName})" : $"{Nickname} ({ShortName})";

        public override string ToString() => DisplayName;
    }
}

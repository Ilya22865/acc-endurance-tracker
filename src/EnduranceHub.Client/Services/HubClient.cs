using System.Net.WebSockets;
using EnduranceHub.Shared.Dto;
using EnduranceHub.Shared.Net;
using Microsoft.AspNetCore.SignalR.Client;

namespace EnduranceHub.Client.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private bool _disposed;

    /// <summary>(подключено, текст статуса) — вызывается при потере и восстановлении связи.</summary>
    public event Action<bool, string>? ConnectionChanged;

    public HubConnectionState State => _connection.State;

    public HubClient(string baseUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hub/session", options =>
            {
                // VPN-клиенты прописывают системный прокси, и SignalR по умолчанию
                // использует его и для negotiate, и для WebSocket. Сервер находится
                // в локальной/оверлейной сети, прокси там не нужен — ходим напрямую.
                options.Proxy = NoProxy.Instance;
                options.HttpMessageHandlerFactory = _ => DirectHttp.CreateHandler();
                options.WebSocketConfiguration = ws =>
                {
                    ws.Proxy = NoProxy.Instance;
                    // По умолчанию 30 с: мёртвый после смены сети канал замечаем быстрее.
                    ws.KeepAliveInterval = TimeSpan.FromSeconds(10);
                };
            })
            // Раньше было 3 попытки (0/2/5 с), после чего клиент навсегда оставался отключённым
            // даже после возвращения сервера. Теперь пытаемся бесконечно.
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _connection.Reconnecting += _ =>
        {
            ConnectionChanged?.Invoke(false, "Связь потеряна, переподключение…");
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            ConnectionChanged?.Invoke(true, "Подключено");
            return Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            ConnectionChanged?.Invoke(false, "Соединение закрыто");
            return Task.CompletedTask;
        };
    }

    public void OnSnapshot(Action<SessionSnapshotDto> handler)
    {
        _connection.On<SessionSnapshotDto>("StateSnapshot", handler);
        _connection.On<SessionSnapshotDto>("StateUpdate", handler);
    }

    public void OnTelemetry(Action<TelemetryBatchDto> handler)
    {
        _connection.On<TelemetryBatchDto>("TelemetryBatch", handler);
    }

    public void OnOutlineRecorded(Action<IReadOnlyList<OutlinePointDto>> handler)
    {
        _connection.On<IReadOnlyList<OutlinePointDto>>("OutlineRecorded", handler);
    }

    public Func<Task>? OnReconnected
    {
        set => _connection.Reconnected += _ => value?.Invoke() ?? Task.CompletedTask;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
        => await _connection.StartAsync(ct);

    /// <summary>
    /// Принудительно переподключается (после смены сети/VPN). Нужно потому, что
    /// автоматическое переподключение замечает мёртвый канал только по таймауту
    /// сервера, а новый маршрут может заработать сразу.
    /// </summary>
    public async Task ForceReconnectAsync(CancellationToken ct = default)
    {
        await _reconnectGate.WaitAsync(ct);
        try
        {
            if (_disposed) return;

            if (_connection.State != HubConnectionState.Disconnected)
            {
                try { await _connection.StopAsync(ct); }
                catch (Exception) { /* старый канал уже мёртв — не мешает новому */ }
            }

            await _connection.StartAsync(ct);
            ConnectionChanged?.Invoke(true, "Подключено");
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _connection.DisposeAsync();
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        // 1, 2, 3, 4, 5, 5, 5… секунд
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
            => TimeSpan.FromSeconds(Math.Min(5, retryContext.PreviousRetryCount + 1));
    }
}

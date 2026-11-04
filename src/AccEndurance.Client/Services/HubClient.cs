using AccEndurance.Shared.Dto;
using Microsoft.AspNetCore.SignalR.Client;

namespace AccEndurance.Client.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    /// <summary>(подключено, текст статуса) — вызывается при потере и восстановлении связи.</summary>
    public event Action<bool, string>? ConnectionChanged;

    public HubClient(string baseUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hub/session")
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

    public async ValueTask DisposeAsync()
        => await _connection.DisposeAsync();

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        // 1, 2, 3, 4, 5, 5, 5… секунд
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
            => TimeSpan.FromSeconds(Math.Min(5, retryContext.PreviousRetryCount + 1));
    }
}

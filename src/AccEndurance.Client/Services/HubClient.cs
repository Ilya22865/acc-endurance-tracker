using AccEndurance.Shared.Dto;
using Microsoft.AspNetCore.SignalR.Client;

namespace AccEndurance.Client.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public HubClient(string baseUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hub/session")
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) })
            .Build();
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
}
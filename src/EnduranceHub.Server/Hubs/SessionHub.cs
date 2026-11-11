using EnduranceHub.Shared.Dto;
using EnduranceHub.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace EnduranceHub.Server.Hubs;

public sealed class SessionHub : Hub
{
    private readonly SessionStateService _sessionState;

    public SessionHub(SessionStateService sessionState)
    {
        _sessionState = sessionState;
    }

    public override async Task OnConnectedAsync()
    {
        var snapshot = _sessionState.BuildSnapshot();
        await Clients.Caller.SendAsync("StateSnapshot", snapshot);
        var outline = _sessionState.RecordedOutline;
        if (outline != null)
            await Clients.Caller.SendAsync("OutlineRecorded", outline);
        await base.OnConnectedAsync();
    }

    public void SubscribeToUpdates()
    {
    }
}

public sealed class SessionStateBroadcaster : BackgroundService
{
    private readonly SessionStateService _sessionState;
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly ILogger<SessionStateBroadcaster> _logger;
    private readonly int _broadcastIntervalMs;
    private long _lastOutlineRevision = -1;

    public SessionStateBroadcaster(
        SessionStateService sessionState,
        IHubContext<SessionHub> hubContext,
        ILogger<SessionStateBroadcaster> logger,
        IConfiguration configuration)
    {
        _sessionState = sessionState;
        _hubContext = hubContext;
        _logger = logger;
        _broadcastIntervalMs = configuration.GetValue<int>("Acc:UpdateIntervalMs", 100);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_sessionState.IsSessionActive)
                {
                    var snapshot = _sessionState.BuildSnapshot();
                    await _hubContext.Clients.All.SendAsync("StateUpdate", snapshot, stoppingToken);
                }

                // Контур нужен и вне активной сессии (например, в меню ACC), поэтому
                // проверяем ревизию независимо от IsSessionActive.
                if (_sessionState.OutlineRevision != _lastOutlineRevision)
                {
                    _lastOutlineRevision = _sessionState.OutlineRevision;
                    var outline = _sessionState.RecordedOutline;
                    if (outline != null)
                        await _hubContext.Clients.All.SendAsync("OutlineRecorded", outline, stoppingToken);
                }
                await Task.Delay(_broadcastIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Broadcast error");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
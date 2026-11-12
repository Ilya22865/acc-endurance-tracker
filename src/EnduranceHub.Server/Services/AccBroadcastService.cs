using EnduranceHub.Server.Models;
using EnduranceHub.Shared.Broadcasting;

namespace EnduranceHub.Server.Services;

public sealed class AccBroadcastService : BackgroundService
{
    private readonly ILogger<AccBroadcastService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SessionStateService _sessionState;
    private BroadcastingProtocol? _protocol;

    public AccBroadcastService(
        ILogger<AccBroadcastService> logger,
        IConfiguration configuration,
        SessionStateService sessionState)
    {
        _logger = logger;
        _configuration = configuration;
        _sessionState = sessionState;

    }

    public bool IsConnected { get; private set; }
    public BroadcastingProtocol? Protocol => _protocol;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ACC broadcast connection error. Reconnecting in 3s...");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }

    private async Task ConnectAndListenAsync(CancellationToken ct)
    {
        var ip = _configuration.GetValue<string>("Acc:Ip") ?? "127.0.0.1";
        var port = _configuration.GetValue<int>("Acc:Port", 9000);
        var displayName = _configuration.GetValue<string>("Acc:DisplayName") ?? "EnduranceHubServer";
        var connectionPassword = _configuration.GetValue<string>("Acc:Password") ?? "";
        var commandPassword = _configuration.GetValue<string>("Acc:CommandPassword") ?? "";
        var msInterval = _configuration.GetValue<int>("Acc:UpdateIntervalMs", 100);

        _logger.LogInformation("Connecting to ACC at {Ip}:{Port}...", ip, port);

        
        _protocol = new BroadcastingProtocol(ip, port, displayName, connectionPassword, commandPassword, msInterval);

        _protocol.OnConnectionStateChanged += msg =>
        {
            _logger.LogInformation("ACC connection: {Message}", msg);
            IsConnected = msg.StartsWith("Connected");
        };

        _protocol.OnEntrylistUpdate += car =>
        {
            _logger.LogDebug("Entry list car: #{RaceNumber} {TeamName} ({ModelName})",
                car.RaceNumber, car.TeamName, CarModels.GetModelName(car.CarModelType));
            _sessionState.UpdateEntryListCar(car);
        };

        _protocol.OnEntryListIndices += indices =>
        {
            _sessionState.PruneEntryList(indices);
        };

        _protocol.OnRealtimeUpdate += update =>
        {
            _sessionState.ProcessRealtimeUpdate(update);
        };

        _protocol.OnRealtimeCarUpdate += update =>
        {
            _sessionState.ProcessRealtimeCarUpdate(update);
        };

        _protocol.OnTrackDataUpdate += track =>
        {
            _logger.LogInformation("Track: {TrackName} ({TrackMeters}m)", track.TrackName, track.TrackMeters);
            _sessionState.SetTrackData(track);
        };

        _protocol.OnBroadcastingEvent += evt =>
        {
            _logger.LogInformation("Event: {Type} — {Msg}", evt.Type, evt.Msg);
            _sessionState.ProcessBroadcastingEvent(evt);
        };

        // события подключены — теперь можно начинать приём
        _protocol.Start();

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            _protocol.Dispose();
            _protocol = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _protocol?.Dispose();
        _protocol = null;
        await base.StopAsync(cancellationToken);
    }
}

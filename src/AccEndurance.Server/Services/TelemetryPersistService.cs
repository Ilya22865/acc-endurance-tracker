using AccEndurance.Domain;
using AccEndurance.Domain.Entities;
using AccEndurance.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AccEndurance.Server.Services;

public sealed class TelemetryPersistService : BackgroundService
{
    private readonly SessionStateService _sessionState;
    private readonly IDbContextFactory<AccEnduranceDbContext> _dbFactory;
    private readonly ILogger<TelemetryPersistService> _logger;
    private readonly TimeSpan _flushInterval;

    public TelemetryPersistService(
        SessionStateService sessionState,
        IDbContextFactory<AccEnduranceDbContext> dbFactory,
        ILogger<TelemetryPersistService> logger,
        IConfiguration configuration)
    {
        _sessionState = sessionState;
        _dbFactory = dbFactory;
        _logger = logger;
        _flushInterval = TimeSpan.FromMilliseconds(configuration.GetValue<int>("Telemetry:FlushIntervalMs", 1000));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_flushInterval, stoppingToken);
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telemetry persist error");
            }
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var pending = _sessionState.DrainPendingTelemetry();
        if (pending.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var stint = await db.Stints
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(s => s.EndedAt == null, ct);

        var driver = !string.IsNullOrEmpty(_sessionState.ActiveDriverName)
            ? await db.Drivers.FirstOrDefaultAsync(
                d => d.ShortName == _sessionState.ActiveDriverName, ct)
            : null;

        int? driverId = driver?.Id;
        int? stintId = stint?.Id;

        db.TelemetrySamples.AddRange(pending.Select(p => new TelemetrySample
        {
            DriverId = driverId,
            StintId = stintId,
            Timestamp = p.Timestamp,
            Gas = p.Gas,
            Brake = p.Brake,
            SteerAngle = p.SteerAngle,
        }));

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Persisted {Count} telemetry samples", pending.Count);
    }
}
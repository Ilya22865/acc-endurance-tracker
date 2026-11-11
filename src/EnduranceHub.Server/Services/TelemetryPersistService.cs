using EnduranceHub.Domain;
using EnduranceHub.Domain.Entities;
using EnduranceHub.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EnduranceHub.Server.Services;

public sealed class TelemetryPersistService : BackgroundService
{
    private readonly SessionStateService _sessionState;
    private readonly IDbContextFactory<EnduranceHubDbContext> _dbFactory;
    private readonly ILogger<TelemetryPersistService> _logger;
    private readonly TimeSpan _flushInterval;

    public TelemetryPersistService(
        SessionStateService sessionState,
        IDbContextFactory<EnduranceHubDbContext> dbFactory,
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

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // запасной вариант для сэмплов, пришедших до того, как стинт получил Id
            var openStintId = await db.Stints
                .Where(s => s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(ct);

            // Пилот и стинт берутся из самого сэмпла (на момент приёма). Раньше использовались
            // «текущий пилот/стинт» на момент записи в БД, и сэмплы последней секунды старого стинта
            // записывались на нового пилота.
            var names = pending.Select(p => p.DriverId).Distinct().ToList();
            var driverRows = await db.Drivers
                .Where(d => names.Contains(d.ShortName))
                .Select(d => new { d.ShortName, d.Id })
                .ToListAsync(ct);
            var driverIds = driverRows
                .GroupBy(d => d.ShortName)
                .ToDictionary(g => g.Key, g => g.First().Id);

            db.TelemetrySamples.AddRange(pending.Select(p => new TelemetrySample
            {
                DriverId = driverIds.TryGetValue(p.DriverId, out var did) ? did : (int?)null,
                StintId = p.StintId != 0 ? p.StintId : openStintId,
                Timestamp = p.Timestamp,
                Gas = p.Gas,
                Brake = p.Brake,
                SteerAngle = p.SteerAngle,
            }));

            await db.SaveChangesAsync(ct);
            _logger.LogDebug("Persisted {Count} telemetry samples", pending.Count);
        }
        catch
        {
            // не теряем уже вычитанную из очереди телеметрию, если БД временно недоступна
            _sessionState.RequeueTelemetry(pending);
            throw;
        }
    }
}

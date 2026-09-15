using System.Net.Http.Json;

namespace AccEndurance.Agent;

public sealed class ServerClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _agentId;

    public ServerClient(string baseUrl, string agentId)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _agentId = agentId;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<List<TeamDriverDto>> GetTeamDriversAsync(CancellationToken ct = default)
    {
        var raw = await _http.GetFromJsonAsync<List<TeamDriverDto>>($"{_baseUrl}/api/drivers/team", ct);
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
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/telemetry/register", payload, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task SendBatchAsync(IReadOnlyList<TelemetryReading> readings, string driverId, CancellationToken ct = default)
    {
        if (readings.Count == 0) return;

        var payload = new
        {
            DriverId = driverId,
            Samples = readings.Select(r => new
            {
                r.PacketId,
                r.Gas,
                r.Brake,
                r.SteerAngle,
            }).ToList(),
        };

        try
        {
            using var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/telemetry/batch", payload, ct);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[warn] Отправка батча не удалась: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
        }
    }

    public void Dispose() => _http.Dispose();

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
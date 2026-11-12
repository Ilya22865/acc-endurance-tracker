using System.Net.Http.Json;
using System.Net.NetworkInformation;
using EnduranceHub.Shared.Dto;
using EnduranceHub.Shared.Net;

namespace EnduranceHub.Client.Services;

public sealed class ApiClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly object _gate = new();
    private readonly string _baseUrl;
    private HttpClient _http;
    private bool _disposed;

    public ApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = DirectHttp.CreateClient(RequestTimeout);
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    // Включение/выключение VPN или смена Wi-Fi/Ethernet делают старые keep-alive
    // соединения мёртвыми. Сбрасываем пул, чтобы следующий запрос открыл
    // соединение по актуальному маршруту (и снова напрямую, без прокси).
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        HttpClient old;
        lock (_gate)
        {
            if (_disposed) return;
            old = _http;
            _http = DirectHttp.CreateClient(RequestTimeout);
        }

        // Не рвём запрос, который уже выполняется: даём ему завершиться.
        _ = Task.Delay(TimeSpan.FromSeconds(5))
            .ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
    }

    private HttpClient Http
    {
        get { lock (_gate) return _http; }
    }

    public async Task<SessionSnapshotDto?> GetSnapshotAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<SessionSnapshotDto>($"{_baseUrl}/api/live/snapshot", ct);

    public async Task<List<DriverDto>> GetTeamDriversAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<DriverDto>>($"{_baseUrl}/api/drivers/team", ct) ?? new();

    public async Task<List<StintDto>> GetStintsAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<StintDto>>($"{_baseUrl}/api/stints", ct) ?? new();

    public async Task<bool> PatchStintAsync(int id, string? notes, double? fuelLiters, int? laps,
        CancellationToken ct = default)
    {
        var resp = await Http.PatchAsJsonAsync(
            $"{_baseUrl}/api/stints/{id}",
            new { notes, plannedFuelLiters = fuelLiters, plannedLaps = laps },
            ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<SetupCarDto>> GetSetupCarsAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<SetupCarDto>>($"{_baseUrl}/api/setups", ct) ?? new();

    public async Task<List<SetupTrackDto>> GetSetupTracksAsync(string car, CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<SetupTrackDto>>($"{_baseUrl}/api/setups/{Uri.EscapeDataString(car)}", ct) ?? new();

    public async Task<List<SetupFileDto>> GetSetupFilesAsync(string car, string track, CancellationToken ct = default)
        => await Http.GetFromJsonAsync<List<SetupFileDto>>($"{_baseUrl}/api/setups/browse/{Uri.EscapeDataString(car)}/{Uri.EscapeDataString(track)}", ct) ?? new();

    public async Task<string?> GetSetupAsync(string car, string track, string file, CancellationToken ct = default)
    {
        var resp = await Http.GetAsync($"{_baseUrl}/api/setups/{Uri.EscapeDataString(car)}/{Uri.EscapeDataString(track)}/{Uri.EscapeDataString(file)}", ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : null;
    }

    public async Task<SetupBaseDto?> GetSetupBasePathAsync(CancellationToken ct = default)
        => await Http.GetFromJsonAsync<SetupBaseDto>($"{_baseUrl}/api/setups/base-path", ct);

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
}

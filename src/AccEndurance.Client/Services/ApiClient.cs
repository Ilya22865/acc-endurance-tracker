using System.Net.Http.Json;
using AccEndurance.Shared.Dto;

namespace AccEndurance.Client.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string _baseUrl;

    public ApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<SessionSnapshotDto?> GetSnapshotAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<SessionSnapshotDto>($"{_baseUrl}/api/live/snapshot", ct);

    public async Task<List<StintDto>> GetStintsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<StintDto>>($"{_baseUrl}/api/stints", ct) ?? new();

    public async Task<bool> PatchStintAsync(int id, string? notes, double? fuelLiters, int? laps,
        CancellationToken ct = default)
    {
        var resp = await _http.PatchAsJsonAsync(
            $"{_baseUrl}/api/stints/{id}",
            new { notes, plannedFuelLiters = fuelLiters, plannedLaps = laps },
            ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<SetupCarDto>> GetSetupCarsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<SetupCarDto>>($"{_baseUrl}/api/setups", ct) ?? new();

    public async Task<List<SetupTrackDto>> GetSetupTracksAsync(string car, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<SetupTrackDto>>($"{_baseUrl}/api/setups/{Uri.EscapeDataString(car)}", ct) ?? new();

    public async Task<List<SetupFileDto>> GetSetupFilesAsync(string car, string track, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<SetupFileDto>>($"{_baseUrl}/api/setups/browse/{Uri.EscapeDataString(car)}/{Uri.EscapeDataString(track)}", ct) ?? new();

    public async Task<string?> GetSetupAsync(string car, string track, string file, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_baseUrl}/api/setups/{Uri.EscapeDataString(car)}/{Uri.EscapeDataString(track)}/{Uri.EscapeDataString(file)}", ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : null;
    }

    public async Task<SetupBaseDto?> GetSetupBasePathAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<SetupBaseDto>($"{_baseUrl}/api/setups/base-path", ct);
}
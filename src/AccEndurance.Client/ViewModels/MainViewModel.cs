using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using AccEndurance.Client.Diagnostics;
using AccEndurance.Client.Services;
using AccEndurance.Shared;
using AccEndurance.Shared.Dto;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccEndurance.Client.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly string _baseUrl;
    private ApiClient? _api;
    private HubClient? _hub;
    private readonly Dictionary<int, StintRow> _stintById = new();

    public ObservableCollection<StandingsRow> Standings { get; } = new();
    public ObservableCollection<StintRow> Stints { get; } = new();
    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem { Title = "Позиции", Icon = Geometry.Parse("M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01") },
        new NavItem { Title = "Стинты", Icon = Geometry.Parse("M20 13 A8 8 0 1 1 4 13 A8 8 0 1 1 20 13 M12 9v4l2.5 2.5M9.5 2h5") },
        new NavItem { Title = "Телеметрия", Icon = Geometry.Parse("M3 12h4l3-8 4 16 3-8h4") },
        new NavItem { Title = "Радар", Icon = Geometry.Parse("M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M17 12 A5 5 0 1 1 7 12 A5 5 0 1 1 17 12 M12 12 L18 6") },
        new NavItem { Title = "Сетап", Icon = Geometry.Parse("M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1 M17 6 A2 2 0 1 1 13 6 A2 2 0 1 1 17 6 M11 12 A2 2 0 1 1 7 12 A2 2 0 1 1 11 12 M19 18 A2 2 0 1 1 15 18 A2 2 0 1 1 19 18") },
        new NavItem { Title = "Карта", Icon = Geometry.Parse("M9 4 L3 6.5 v13 L9 17 l6 3 6-2.5 v-13 L15 7 L9 4 Z M9 4v13 M15 7v13") },
        new NavItem { Title = "Сервер", IsGroupStart = true, Icon = Geometry.Parse("M5 4 H19 A2 2 0 0 1 21 6 V8 A2 2 0 0 1 19 10 H5 A2 2 0 0 1 3 8 V6 A2 2 0 0 1 5 4 Z M5 14 H19 A2 2 0 0 1 21 16 V18 A2 2 0 0 1 19 20 H5 A2 2 0 0 1 3 18 V16 A2 2 0 0 1 5 14 Z M7 7h.01M7 17h.01") },
        new NavItem { Title = "Агент", Icon = Geometry.Parse("M4 5 H20 V15 H4 Z M8 19h8M12 15v4") },
        new NavItem { Title = "Настройки", Icon = Geometry.Parse("M12 8 A4 4 0 1 1 12 16 A4 4 0 1 1 12 8 M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M19 5l-2 2M7 17l-2 2") },
    };
    public ObservableCollection<string> SetupCars { get; } = new();
    public ObservableCollection<string> SetupTracks { get; } = new();
    public ObservableCollection<SetupFileRow> SetupFiles { get; } = new();

    [ObservableProperty] private string _connectionStatus = "Не подключено";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _sessionText = "—";
    [ObservableProperty] private string _phaseText = "—";
    [ObservableProperty] private string _timeText = "—";
    [ObservableProperty] private string _ourDriverText = "—";
    [ObservableProperty] private string _serverUrl = "";
    [ObservableProperty] private StintRow? _selectedStint;
    [ObservableProperty] private string? _editNotes;
    [ObservableProperty] private double? _editFuel;
    [ObservableProperty] private int? _editLaps;
    [ObservableProperty] private string _lastUpdate = "";
    [ObservableProperty] private string? _selectedSetupCar;
    [ObservableProperty] private string? _selectedSetupTrack;
    [ObservableProperty] private SetupFileRow? _selectedSetupFile;
    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private bool _isPage0 = true;
    [ObservableProperty] private bool _isPage1;
    [ObservableProperty] private bool _isPage2;
    [ObservableProperty] private bool _isPage3;
    [ObservableProperty] private bool _isPage4;
    [ObservableProperty] private bool _isPage5;
    [ObservableProperty] private bool _isPage6;
    [ObservableProperty] private bool _isPage7;
    [ObservableProperty] private bool _isPage8;
    [ObservableProperty] private bool _isPage9;
    [ObservableProperty] private string _setupJson = "";
    [ObservableProperty] private string _setupStatus = "";

    [ObservableProperty] private string _mapTrackName = "";
    [ObservableProperty] private string _weatherAir = "";
    [ObservableProperty] private string _weatherTrack = "";
    [ObservableProperty] private string _weatherRain = "";
    [ObservableProperty] private string _weatherClouds = "";
    [ObservableProperty] private string _weatherWetness = "";

    // Server & Agent
    [ObservableProperty] private bool _isServerRunning;
    [ObservableProperty] private bool _isAgentRunning;
    [ObservableProperty] private string _serverStatus = "Остановлен";
    [ObservableProperty] private string _agentStatus = "Остановлен";
    [ObservableProperty] private int _selectedDriverIndex;
    [ObservableProperty] private bool _agentUseFakeData;

    // Пути к встроенным Server и Agent фиксированы относительно папки приложения.
    public string ServerExePath => EmbeddedServicePaths.ServerExePath;
    public string AgentExePath => EmbeddedServicePaths.AgentExePath;

    // Live telemetry (страница «Телеметрия»)
    public Services.TelemetryBuffer Telemetry { get; } = new();
    [ObservableProperty] private string _telemetryDriverName = "—";
    [ObservableProperty] private string _telemetryStatus = "Ожидание данных от агента…";
    [ObservableProperty] private string _telemetryRateText = "—";
    [ObservableProperty] private string _gasText = "0%";
    [ObservableProperty] private string _brakeText = "0%";
    [ObservableProperty] private string _steerText = "0";
    [ObservableProperty] private int _telemetryWindowIndex = 1; // 0 = 15 с, 1 = 30 с, 2 = 60 с
    public double TelemetryWindowSeconds => TelemetryWindowIndex switch { 0 => 15, 2 => 60, _ => 30 };
    public ICommand ClearTelemetryCommand { get; }

    // Telemetry Statistics
    [ObservableProperty] private string _telemetryAvgGas = "0%";
    [ObservableProperty] private string _telemetryAvgBrake = "0%";
    [ObservableProperty] private string _telemetryMaxSteer = "0°";
    [ObservableProperty] private string _telemetryBrakeTime = "0%";
    [ObservableProperty] private string _telemetryFullThrottleTime = "0%";

    public ObservableCollection<string> ServerLogLines { get; } = new();
    public ObservableCollection<string> AgentLogLines { get; } = new();

    private readonly Services.EmbeddedServerService? _embeddedServer;
    private readonly Services.EmbeddedAgentService? _embeddedAgent;
    private readonly Services.ManualUpdateService? _updateService;
    private readonly Services.AutoSaveService? _autoSaveService;
    private readonly Services.ExportService? _exportService;
    private readonly Services.TelemetryComparisonService? _telemetryComparisonService;
    private readonly Services.SettingsService? _settingsService;

    // Update UI
    [ObservableProperty] private string _currentVersionText = "";
    [ObservableProperty] private string _latestVersionText = "";
    [ObservableProperty] private string _updateStatus = "Нажмите кнопку для проверки обновлений";
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private int _updateProgress;
    private object? _pendingUpdate;

    // Settings
    [ObservableProperty] private int _ourCarNumber;

    public event Action? MapUpdated;
    private List<(double X, double Y)> _lastOutline = new();
    private List<CarMapDot> _lastDots = new();
    private IReadOnlyList<(double X, double Y)>? _recordedOutline;
    private SessionSnapshotDto? _lastSnapshot;

    public ObservableCollection<SetupSection> SetupSections { get; } = new();

    private string _currentTrackName = "";
    private string _stintsFingerprint = "";
    private DateTime _latestOpenStartedAt = DateTime.UtcNow;

    public ICommand ConnectCommand { get; }
    public ICommand SaveStintCommand { get; }
    public ICommand RefreshSetupsCommand { get; }
    public ICommand ShowSetupCommand { get; }
    public ICommand OpenSetupFolderCommand { get; }
    public ICommand GoToPageCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand StartAgentCommand { get; }
    public ICommand StopAgentCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand ClearAutoSaveCommand { get; }
    public ICommand ExportToJsonCommand { get; }
    public ICommand ExportToCsvCommand { get; }

    public MainViewModel(string baseUrl)
    {
        _baseUrl = baseUrl;
        ServerUrl = baseUrl;
        ConnectCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ConnectAsync);
        SaveStintCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(SaveStintAsync);
        RefreshSetupsCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(LoadSetupsAsync);
        ShowSetupCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ShowSetupAsync);
        OpenSetupFolderCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(OpenSetupFolderAsync);
        GoToPageCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<int>(i => SelectedPageIndex = i);
        StartServerCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(StartServerAsync);
        StopServerCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(StopServerAsync);
        StartAgentCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(StartAgentAsync);
        StopAgentCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(StopAgentAsync);
        CheckForUpdatesCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(CheckForUpdatesAsync);
        InstallUpdateCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(InstallUpdateAsync);
        ClearAutoSaveCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ClearAutoSave);
        ExportToJsonCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ExportToJsonAsync);
        ExportToCsvCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ExportToCsvAsync);
        ClearTelemetryCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => Telemetry.Clear());
        RefreshTeamDriversCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(LoadTeamDriversAsync);

        try
        {
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => { });
            _embeddedServer = new Services.EmbeddedServerService(
                loggerFactory.CreateLogger(typeof(Services.EmbeddedServerService).FullName ?? "EmbeddedServer"));
            _embeddedServer.OnLogLine += line =>
            {
                var text = PrepareLogLine(line);
                // Background: строки лога не должны конкурировать с отрисовкой и вводом,
                // иначе при активном сервере интерфейс начинает «залипать».
                Dispatcher.UIThread.Post(() =>
                {
                    ServerLogLines.Add(text);
                    while (ServerLogLines.Count > 500) ServerLogLines.RemoveAt(0);
                }, DispatcherPriority.Background);
            };
            _embeddedServer.OnStateChanged += running =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsServerRunning = running;
                    ServerStatus = running ? "Запущен" : "Остановлен";
                });
            };

            _embeddedAgent = new Services.EmbeddedAgentService(
                loggerFactory.CreateLogger(typeof(Services.EmbeddedAgentService).FullName ?? "EmbeddedAgent"));
            _embeddedAgent.OnLogLine += line =>
            {
                var text = PrepareLogLine(line);
                Dispatcher.UIThread.Post(() =>
                {
                    AgentLogLines.Add(text);
                    while (AgentLogLines.Count > 500) AgentLogLines.RemoveAt(0);
                }, DispatcherPriority.Background);
            };
            _embeddedAgent.OnStateChanged += running =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsAgentRunning = running;
                    AgentStatus = running ? "Запущен" : "Остановлен";
                });
            };

            // Update service
            _updateService = new Services.ManualUpdateService(
                loggerFactory.CreateLogger(typeof(Services.ManualUpdateService).FullName ?? "UpdateService"));
            _updateService.OnStatusChanged += status =>
            {
                Dispatcher.UIThread.Post(() => UpdateStatus = status);
            };
            _updateService.OnProgressChanged += progress =>
            {
                Dispatcher.UIThread.Post(() => UpdateProgress = progress);
            };

            CurrentVersionText = _updateService.CurrentVersion;
            LatestVersionText = "—";

            // AutoSave service
            _autoSaveService = new Services.AutoSaveService();

            // Export service
            _exportService = new Services.ExportService();

            // Telemetry comparison service
            _telemetryComparisonService = new Services.TelemetryComparisonService();

            // Settings service
            _settingsService = new Services.SettingsService();
            var settings = _settingsService.LoadSettings();
            OurCarNumber = settings.OurCarNumber;
            RadarMirror = settings.RadarMirror;
            RadarRangeIndex = Math.Clamp(settings.RadarRangeIndex, 0, 2);
            AgentUseFakeData = settings.AgentUseFakeData;
            _settingsLoaded = true;

            // Попытка восстановить последнюю сессию (только если подключение не удалось)
            var restoredSnapshot = _autoSaveService.LoadLastSession();
            if (restoredSnapshot != null && !string.IsNullOrEmpty(ServerUrl))
            {
                // Не восстанавливаем автоматически - пользователь должен подключиться
                DebugLog.Write("AutoSave: snapshot found but not restored (connect manually)");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Failed to init embedded services: {ex}");
        }
    }

    private const int MaxLogLineLength = 2000;

    /// <summary>Слишком длинная строка (например, SQL-дамп) раздувает элемент списка и тормозит прокрутку.</summary>
    private static string PrepareLogLine(string line)
        => line.Length <= MaxLogLineLength ? line : line[..MaxLogLineLength] + " …";

    private async Task StartServerAsync()
    {
        if (string.IsNullOrEmpty(ServerExePath))
        {
            ServerLogLines.Add("❌ Путь к AccEndurance.Server.exe не указан");
            return;
        }
        await (_embeddedServer?.StartAsync(ServerExePath) ?? Task.CompletedTask);
    }

    private async Task StopServerAsync()
    {
        // stopStaleProcesses: true — на случай, если после прошлого аварийного
        // закрытия остался сервер, который держит порт 5261.
        await (_embeddedServer?.StopAsync(stopStaleProcesses: true) ?? Task.CompletedTask);
    }

    private async Task StartAgentAsync()
    {
        if (string.IsNullOrEmpty(AgentExePath))
        {
            AgentLogLines.Add("❌ Путь к AccEndurance.Agent.exe не указан");
            return;
        }
        await (_embeddedAgent?.StartAsync(AgentExePath, NormalizeServerUrl(ServerUrl), SelectedDriverIndex, useFakeData: AgentUseFakeData) ?? Task.CompletedTask);
    }

    private async Task StopAgentAsync()
    {
        // Останавливаем и «осиротевшие» агенты прошлых запусков,
        // иначе они продолжают присылать телеметрию параллельно с новым.
        await (_embeddedAgent?.StopAsync(stopStaleProcesses: true) ?? Task.CompletedTask);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateService == null) return;

        IsCheckingUpdate = true;
        IsUpdateAvailable = false;
        _pendingUpdate = null;

        var result = await _updateService.CheckForUpdatesAsync();

        CurrentVersionText = result.CurrentVersion;
        LatestVersionText = result.LatestVersion;
        IsUpdateAvailable = result.IsUpdateAvailable;
        _pendingUpdate = result.UpdateInfo;

        IsCheckingUpdate = false;
    }

    private async Task InstallUpdateAsync()
    {
        if (_updateService == null || _pendingUpdate == null) return;

        var success = await _updateService.DownloadAndApplyUpdateAsync(_pendingUpdate);
        if (!success)
        {
            UpdateStatus = "❌ Не удалось установить обновление";
        }
    }

    private bool _connecting;

    public async Task ConnectAsync()
    {
        if (_connecting) return;
        _connecting = true;
        try
        {
            // Первое подключение не удалось (сервер ещё не запущен) — повторяем, а не остаёмся
            // навсегда в состоянии «Ошибка».
            while (!await TryConnectOnceAsync())
                await Task.Delay(TimeSpan.FromSeconds(3));
        }
        finally
        {
            _connecting = false;
        }
    }

    private async Task<bool> TryConnectOnceAsync()
    {
        ConnectionStatus = "Подключение…";
        HubClient? hub = null;
        try
        {
            var url = NormalizeServerUrl(ServerUrl);
            ServerUrl = url;
            _api = new ApiClient(url);
            var initial = await _api.GetSnapshotAsync();
            if (initial == null)
            {
                ConnectionStatus = "Нет ответа от сервера";
                return false;
            }

            var old = _hub;
            _hub = null;
            if (old != null)
            {
                try { await old.DisposeAsync(); } catch { }
            }

            hub = new HubClient(url);
            hub.OnSnapshot(snapshot => Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot)));
            hub.OnOutlineRecorded(points => Dispatcher.UIThread.Post(() => ApplyRecordedOutline(points)));
            hub.OnTelemetry(batch => Dispatcher.UIThread.Post(() => ApplyTelemetry(batch)));
            hub.ConnectionChanged += (ok, text) => Dispatcher.UIThread.Post(() =>
            {
                IsConnected = ok;
                ConnectionStatus = text;
                if (!ok) ClearAllData();
            });
            await hub.ConnectAsync();

            _hub = hub;
            IsConnected = true;
            ConnectionStatus = "Подключено";
            ApplySnapshot(initial);
            SaveServerUrl(url);
            _ = LoadSetupsAsync();
            _ = LoadTeamDriversAsync();
            return true;
        }
        catch (Exception ex)
        {
            if (hub != null)
            {
                try { await hub.DisposeAsync(); } catch { }
            }
            IsConnected = false;
            ConnectionStatus = $"Ошибка: {ex.Message}";
            ClearAllData();
            return false;
        }
    }

    private void ApplySnapshot(SessionSnapshotDto snapshot)
    {
        _lastSnapshot = snapshot;
        DebugLog.Write($"snapshot: cars={snapshot.Cars.Count} stints={snapshot.Stints.Count} cur={snapshot.CurrentStint?.Driver}");

        // Обновляем автосохранение
        _autoSaveService?.UpdateSnapshot(snapshot);

        try
        {
            var rt = snapshot.Realtime;
        var newTrackName = rt.TrackName ?? "";
        if (!string.Equals(_currentTrackName, newTrackName, StringComparison.OrdinalIgnoreCase))
        {
            // при смене трассы старый записанный контур больше не подходит;
            // новый придёт из OutlineRecorded, если сервер нашёл сохранённый файл
            _recordedOutline = null;
        }
        _currentTrackName = newTrackName;
        MapTrackName = _currentTrackName;

        SessionText = $"{SessionTypeName(rt.SessionType)} · {rt.TrackName ?? "трек"}";
        TrackTitle = string.IsNullOrWhiteSpace(rt.TrackName) ? "ACC Endurance" : rt.TrackName;
        SessionTypeText = SessionTypeName(rt.SessionType);
        PhaseText = PhaseName(rt.Phase);
        IsRaceSession = rt.IsRace;
        ApplyClock(rt);
        ApplyFlag(rt);

        WeatherAir = $"{rt.AmbientTemp}°C";
        WeatherTrack = $"{rt.TrackTemp}°C";
        WeatherRain = $"{rt.RainLevel * 100:F0}%";
        WeatherClouds = $"{rt.Clouds * 100:F0}%";
        WeatherWetness = $"{rt.Wetness * 100:F0}%";

        UpdateStandings(snapshot.Cars, rt);
        RecordGapHistory(snapshot.Cars, rt.IsRace);
        UpdateStints(snapshot);
        UpdateMap(snapshot);

        var our = snapshot.Cars.FirstOrDefault(c => c.IsOurCar);
        OurDriverText = our != null ? $"#{our.RaceNumber} · {our.Driver} · P{our.Position} · {our.Laps} кр." : "—";
        OwnNumberText = our != null ? our.RaceNumber.ToString() : "—";
        OwnDriverName = our != null && !string.IsNullOrWhiteSpace(our.Driver) ? our.Driver : "Наша машина не найдена";
        OwnStatusText = our != null ? $"P{our.Position} · {our.Laps} кр." + (snapshot.CurrentStint != null ? " · стинт " + FormatDuration(DateTime.UtcNow, snapshot.CurrentStint.StartedAt) : "") : "";

        // Раньше сюда записывался индекс МАШИНЫ (CarIndex) вместо индекса пилота и затирал выбор пользователя на каждом снимке.
        // Теперь пилот выбирается в окне «Агент» из списка команды.
        if (TeamDrivers.Count == 0 && our != null && DateTime.UtcNow - _lastTeamDriversTry > TimeSpan.FromSeconds(5))
            _ = LoadTeamDriversAsync();

        if (IsPage3) UpdateRadar(snapshot);

        LastUpdate = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            DebugLog.Write("ApplySnapshot ERROR: " + ex);
        }
    }

    private void UpdateStints(SessionSnapshotDto snapshot)
    {
        var rows = new List<StintRow>();

        foreach (var s in snapshot.Stints)
        {
            var open = s.EndedAt == null;
            rows.Add(new StintRow
            {
                StintId = s.Id,
                Driver = s.Driver,
                DriverIndex = s.DriverIndex,
                Ordinal = rows.Count + 1,
                LapNumber = s.LapNumber,
                StartReal = s.StartedAt.ToLocalTime().ToString("HH:mm:ss"),
                StartGame = FormatClock(s.StartSessionTime),
                EndReal = s.EndedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—",
                EndGame = s.EndSessionTime is TimeSpan endGame ? FormatClock(endGame) : "—",
                IsOpen = open,
                Duration = FormatDuration(s.EndedAt ?? DateTime.UtcNow, s.StartedAt),
                Notes = s.Notes,
                PlannedFuelLiters = s.PlannedFuelLiters,
                PlannedLaps = s.PlannedLaps,
            });
        }

        if (snapshot.CurrentStint != null && rows.All(r => r.StintId != snapshot.CurrentStint.Id))
        {
            rows.Add(new StintRow
            {
                StintId = snapshot.CurrentStint.Id,
                Driver = snapshot.CurrentStint.Driver,
                DriverIndex = snapshot.CurrentStint.DriverIndex,
                Ordinal = rows.Count + 1,
                LapNumber = snapshot.CurrentStint.LapNumber,
                StartReal = snapshot.CurrentStint.StartedAt.ToLocalTime().ToString("HH:mm:ss"),
                StartGame = FormatClock(snapshot.CurrentStint.StartSessionTime),
                IsOpen = true,
                Duration = FormatDuration(DateTime.UtcNow, snapshot.CurrentStint.StartedAt),
                Notes = snapshot.CurrentStint.Notes,
                PlannedFuelLiters = snapshot.CurrentStint.PlannedFuelLiters,
                PlannedLaps = snapshot.CurrentStint.PlannedLaps,
            });
        }

        var openStint = snapshot.CurrentStint ?? snapshot.Stints.FirstOrDefault(s => s.EndedAt == null);
        if (openStint != null) _latestOpenStartedAt = openStint.StartedAt;

        var fingerprint = string.Join('|', rows.Select(r =>
            $"{r.StintId};{r.Ordinal};{r.StartReal};{r.StartGame};{r.EndReal};{r.EndGame};{r.IsOpen};{r.Notes};{r.PlannedFuelLiters};{r.PlannedLaps}"));
        if (fingerprint == _stintsFingerprint)
        {
            var openRow = Stints.FirstOrDefault(r => r.IsOpen);
            if (openRow != null) openRow.Duration = FormatDuration(DateTime.UtcNow, _latestOpenStartedAt);
            return;
        }
        _stintsFingerprint = fingerprint;

        var selectedIdStable = SelectedStint?.StintId;

        Stints.Clear();
        foreach (var r in rows)
            Stints.Add(r);

        // keep the previously selected stint (if still present), else select the open/latest one
        SelectedStint = selectedIdStable is int id && Stints.Any(r => r.StintId == id)
            ? Stints.First(r => r.StintId == id)
            : Stints.LastOrDefault(r => r.IsOpen) ?? Stints.LastOrDefault();
    }

    private void UpdateMap(SessionSnapshotDto snapshot)
    {
        var rt = snapshot.Realtime;
        // записанный контур (живая геометрия) имеет приоритет над схемой
        var outline = _recordedOutline ?? TrackLayouts.GetOutline(rt.TrackName, rt.TrackMeters);

        _lastOutline = outline.ToList();
        _lastDots = snapshot.Cars.Select(c =>
        {
            var (x, y) = TrackLayouts.InterpolatePosition(outline, c.SplinePosition);
            return new CarMapDot
            {
                X = x,
                Y = y,
                RaceNumber = c.RaceNumber,
                Driver = c.Driver,
                IsOurCar = c.IsOurCar,
            };
        }).ToList();

        MapUpdated?.Invoke();
    }

    public void ApplyRecordedOutline(IReadOnlyList<OutlinePointDto> points)
    {
        if (points == null || points.Count < 2)
        {
            DebugLog.Write("recorded outline: empty, ignoring");
            return;
        }
        DebugLog.Write($"recorded outline: {points.Count} pts");
        _recordedOutline = points.Select(p => (p.X, p.Y)).ToList();
        if (_lastSnapshot != null) UpdateMap(_lastSnapshot);
        else MapUpdated?.Invoke();
    }

    public List<(double X, double Y)> CurrentOutline => _lastOutline;
    public List<CarMapDot> CurrentDots => _lastDots;

    private async Task LoadSetupsAsync()
    {
        if (_api == null) return;
        try
        {
            SetupStatus = "Загрузка списка автомобилей…";
            var cars = await _api.GetSetupCarsAsync();
            SetupCars.Clear();
            foreach (var c in cars) SetupCars.Add(c.CarFolder);

            if (SetupCars.Count == 0)
            {
                SetupStatus = "Сетапы на сервере не найдены (нет папок DOCUMENTS/…/Setups/<car>).";
                SetupJson = "";
                SetupSections.Clear();
                return;
            }

            SelectedSetupCar = null;
            SelectedSetupTrack = null;
            SelectedSetupCar = SetupCars.FirstOrDefault() ?? SetupCars[0];
            SetupStatus = "Выберите автомобиль и сетап.";
        }
        catch (Exception ex)
        {
            SetupStatus = "Ошибка загрузки сетапов: " + ex.Message;
            DebugLog.Write("LoadSetups ERROR: " + ex);
        }
    }

    partial void OnSelectedSetupCarChanged(string? value)
    {
        _ = LoadTracksAsync(value);
    }

    partial void OnSelectedSetupTrackChanged(string? value)
    {
        _ = LoadSetupFilesAsync(value);
    }

    partial void OnSelectedSetupFileChanged(SetupFileRow? value)
    {
        _ = ShowSetupAsync();
    }

    private async Task LoadTracksAsync(string? car)
    {
        if (_api == null || string.IsNullOrEmpty(car)) return;
        try
        {
            var tracks = await _api.GetSetupTracksAsync(car);
            SetupTracks.Clear();
            foreach (var t in tracks) SetupTracks.Add(t.TrackFolder);

            SelectedSetupTrack = null;
            var match = SetupTracks.FirstOrDefault(t =>
                string.Equals(t, _currentTrackName, StringComparison.OrdinalIgnoreCase));
            SelectedSetupTrack = match ?? SetupTracks.FirstOrDefault();
        }
        catch (Exception ex)
        {
            SetupStatus = "Ошибка загрузки трасс: " + ex.Message;
        }
    }

    private async Task LoadSetupFilesAsync(string? track)
    {
        if (_api == null || string.IsNullOrEmpty(SelectedSetupCar) || string.IsNullOrEmpty(track)) return;
        try
        {
            var files = await _api.GetSetupFilesAsync(SelectedSetupCar, track);
            SetupFiles.Clear();
            foreach (var f in files)
            {
                SetupFiles.Add(new SetupFileRow
                {
                    FileName = f.FileName,
                    ModifiedText = f.ModifiedAt.ToLocalTime().ToString("dd.MM.yy HH:mm"),
                    SizeText = f.SizeBytes >= 1024 * 1024
                        ? $"{f.SizeBytes / 1024.0 / 1024.0:F1} МБ"
                        : $"{f.SizeBytes / 1024.0:F0} КБ",
                });
            }
            SetupStatus = $"{SetupFiles.Count} файлов(а) для {SelectedSetupCar} · {track}";
        }
        catch (Exception ex)
        {
            SetupStatus = "Ошибка загрузки файлов: " + ex.Message;
        }
    }

    private async Task ShowSetupAsync()
    {
        if (_api == null || SelectedSetupFile == null) return;
        var car = SelectedSetupCar;
        var track = SelectedSetupTrack;
        if (string.IsNullOrEmpty(car) || string.IsNullOrEmpty(track)) return;

        try
        {
            SetupStatus = $"Чтение {SelectedSetupFile.FileName}…";
            var json = await _api.GetSetupAsync(car, track, SelectedSetupFile.FileName);
            if (json == null)
            {
                SetupJson = "";
                SetupSections.Clear();
                SetupStatus = "Файл не найден на сервере.";
                return;
            }

            SetupJson = PrettyJson(json);
            var sections = BuildSetupSections(json);
            SetupSections.Clear();
            foreach (var s in sections) SetupSections.Add(s);
            SetupStatus = $"{SelectedSetupFile.FileName} · {SelectedSetupFile.SizeText}";
        }
        catch (Exception ex)
        {
            SetupStatus = "Ошибка чтения сетапа: " + ex.Message;
            DebugLog.Write("ShowSetup ERROR: " + ex);
        }
    }

    private async Task OpenSetupFolderAsync()
    {
        if (_api == null) { SetupStatus = "Не подключено к серверу."; return; }
        try
        {
            var info = await _api.GetSetupBasePathAsync();
            if (info == null || !info.Exists)
            {
                SetupStatus = $"Папка сетапов не найдена на сервере: {info?.BasePath ?? "?"}(на гонящей машине ACC нет).";
                return;
            }

            SetupStatus = $"Папка сетапов: {info.BasePath}";
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(info.BasePath) { UseShellExecute = true });
            }
            else
            {
                using var p = new Process();
                p.StartInfo.FileName = "xdg-open";
                p.StartInfo.ArgumentList.Add(info.BasePath);
                p.Start();
            }
        }
        catch (Exception ex)
        {
            SetupStatus = "Не удалось открыть папку: " + ex.Message;
        }
    }

    private static List<SetupSection> BuildSetupSections(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null) return new List<SetupSection>();

            var sections = new List<SetupSection>();
            var basic = root["basicSetup"] as JsonObject;
            var adv = root["advancedSetup"] as JsonObject;
            var tyres = basic?["tyres"] as JsonObject;
            var alignment = basic?["alignment"] as JsonObject;
            var electronics = basic?["electronics"] as JsonObject;
            var strategy = basic?["strategy"] as JsonObject;
            var mech = adv?["mechanicalBalance"] as JsonObject;
            var dampers = adv?["dampers"] as JsonObject;
            var aero = adv?["aeroBalance"] as JsonObject;
            var drivetrain = adv?["drivetrain"] as JsonObject;

            var carName = root["carName"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(carName))
                sections.Add(new SetupSection { Title = carName, Accent = SolidColorBrush.Parse("#FFC94A"), Items = { new() { Key = "Модель в сетапе", Value = carName } } });

            AppendSection(sections, strategy, "Стратегия", "#2EBD93", new (string, string?)[]
            {
                ("Топливо в руки, л", GetValue(strategy, "fuel")),
                ("Расход, л/круг", GetValue(strategy, "fuelPerLap")),
                ("Остановки", GetValue(strategy, "nPitStops")),
                ("Комплект шин", GetValue(strategy, "tyreSet")),
                ("Колодки П / З", Join2(GetValue(strategy, "frontBrakePadCompound"), GetValue(strategy, "rearBrakePadCompound"))),
            });

            AppendSection(sections, tyres, "Шины", "#4FC3F7", new (string, string?)[]
            {
                ("Состав", GetValue(tyres, "tyreCompound")),
                ("Давление ПЛ ПП ЗЛ ЗП", GetArray(tyres, "tyrePressure")),
            });

            AppendSection(sections, alignment, "Развал / схождение", "#BA68C8", new (string, string?)[]
            {
                ("Camber", GetArray(alignment, "camber")),
                ("Статический развал", GetArray(alignment, "staticCamber")),
                ("Toe", GetArray(alignment, "toe")),
                ("Toe (линейный, рад)", GetArray(alignment, "toeOutLinear")),
                ("Кастор Л / П", Join2(GetValue(alignment, "casterLF"), GetValue(alignment, "casterRF"))),
                ("Рулевое соотношение", GetValue(alignment, "steerRatio")),
            });

            AppendSection(sections, electronics, "Электроника", "#FFB74D", new (string, string?)[]
            {
                ("TC1 / TC2", Join2(GetValue(electronics, "tC1"), GetValue(electronics, "tC2"))),
                ("ABS", GetValue(electronics, "abs")),
                ("Карта двигателя", GetValue(electronics, "eCUMap")),
                ("Смесь топлива", GetValue(electronics, "fuelMix")),
            });

            AppendSection(sections, mech, "Мех. баланс", "#F06292", new (string, string?)[]
            {
                ("Стабилизатор П / З", Join2(GetValue(mech, "aRBFront"), GetValue(mech, "aRBRear"))),
                ("Жёсткость пружин", GetArray(mech, "wheelRate")),
                ("Бампстоп вверх", GetArray(mech, "bumpStopRateUp")),
                ("Бампстоп вниз", GetArray(mech, "bumpStopRateDn")),
                ("Окно бампстопа", GetArray(mech, "bumpStopWindow")),
                ("Тормозной момент", GetValue(mech, "brakeTorque")),
                ("Тормозной баланс", GetValue(mech, "brakeBias")),
            });

            AppendSection(sections, dampers, "Амортизаторы", "#9575CD", new (string, string?)[]
            {
                ("Сжатие медленно", GetArray(dampers, "bumpSlow")),
                ("Сжатие быстро", GetArray(dampers, "bumpFast")),
                ("Отбой медленно", GetArray(dampers, "reboundSlow")),
                ("Отбой быстро", GetArray(dampers, "reboundFast")),
            });

            AppendSection(sections, aero, "Аэробаланс", "#4DB6AC", new (string, string?)[]
            {
                ("Дорожный просвет", GetArray(aero, "rideHeight")),
                ("Длина тяг", GetArray(aero, "rodLength")),
                ("Сплиттер", GetValue(aero, "splitter")),
                ("Заднее крыло", GetValue(aero, "rearWing")),
                ("Тормозные каналы П / З", GetArray(aero, "brakeDuct")),
            });

            AppendSection(sections, drivetrain, "Дифференциал", "#A1887F", new (string, string?)[]
            {
                ("Преднатяг", GetValue(drivetrain, "preload")),
            });

            return sections;
        }
        catch (Exception)
        {
            return new List<SetupSection> { new() { Title = "Ошибка", Accent = SolidColorBrush.Parse("#E53935"), Items = { new() { Key = "JSON", Value = "Не удалось разобрать сетап" } } } };
        }
    }

    private static void AppendSection(List<SetupSection> sections, JsonObject? src, string title, string accent, (string Key, string? Value)[] items)
    {
        if (src == null) return;
        var section = new SetupSection { Title = title, Accent = SolidColorBrush.Parse(accent) };
        foreach (var (key, value) in items)
            if (value != null)
                section.Items.Add(new SetupValue { Key = key, Value = value });
        if (section.Items.Count > 0) sections.Add(section);
    }

    private static string? FormatNode(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<decimal>(out var d)) return d.ToString("0.####");
        if (v.TryGetValue<double>(out var dd)) return dd.ToString("0.####");
        if (v.TryGetValue<string>(out var str)) return str;
        if (v.TryGetValue<bool>(out var b)) return b ? "да" : "нет";
        return v.ToString();
    }

    // Раньше: параметр был не nullable, а секции basicSetup/advancedSetup могли отсутствовать →
    // NullReferenceException, и весь сетап показывался как «Не удалось разобрать».
    // GetValue<decimal>() также падал на нечисловых значениях.
    private static string? GetValue(JsonObject? o, string key)
        => o != null && o.TryGetPropertyValue(key, out var n) ? FormatNode(n) : null;

    private static string? GetArray(JsonObject? o, string key)
    {
        if (o?[key] is not JsonArray a) return null;
        var items = new List<string>();
        foreach (var n in a)
        {
            var text = n is JsonObject item && item.TryGetPropertyValue("value", out var inner)
                ? FormatNode(inner)
                : FormatNode(n);
            if (text != null) items.Add(text);
        }
        return items.Count > 0 ? string.Join(" | ", items) : null;
    }

    private static string? Join2(string? a, string? b = null)
        => a != null && b != null ? $"{a} / {b}" : a ?? b;

    private static string PrettyJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
        }
        catch
        {
            return json;
        }
    }

    private async Task SaveStintAsync()
    {
        if (_api == null || SelectedStint == null) return;
        try
        {
            var ok = await _api.PatchStintAsync(
                SelectedStint.StintId, EditNotes, EditFuel, EditLaps);
            ConnectionStatus = ok ? "Стинт обновлён" : "Не удалось обновить стинт";
        }
        catch (Exception ex)
        {
            // сетевая ошибка раньше уходила необработанной из команды
            ConnectionStatus = "Не удалось обновить стинт: " + ex.Message;
        }
    }

    partial void OnSelectedStintChanged(StintRow? value)
    {
        EditNotes = value?.Notes;
        EditFuel = value?.PlannedFuelLiters;
        EditLaps = value?.PlannedLaps;
    }

    partial void OnSelectedPageIndexChanged(int value)
    {
        IsPage0 = value == 0;
        IsPage1 = value == 1;
        IsPage2 = value == 2;
        IsPage3 = value == 3;
        IsPage4 = value == 4;
        IsPage5 = value == 5;
        IsPage6 = value == 6;
        IsPage7 = value == 7;
        IsPage8 = value == 8;
        IsPage9 = value == 9;
        if (value == 3 && _lastSnapshot != null) UpdateRadar(_lastSnapshot);
    }

    public SessionSnapshotDto? LastSnapshot => _lastSnapshot;

    private void ApplyTelemetry(TelemetryBatchDto batch)
    {
        Telemetry.Add(batch.Samples);
        if (!string.IsNullOrWhiteSpace(batch.Driver))
            TelemetryDriverName = batch.Driver;
    }

    /// <summary>Вызывается таймером окна: обновляет текущие значения и статус.</summary>
    public void RefreshTelemetryTexts()
    {
        var latest = Telemetry.Latest;
        var age = Telemetry.Now - Telemetry.LastSampleAt;
        if (latest is TelemetryPoint p && age < 2.0)
        {
            GasText = $"{p.Gas * 100:F0}%";
            BrakeText = $"{p.Brake * 100:F0}%";
            GasPercent = Math.Clamp(p.Gas * 100, 0, 100);
            BrakePercent = Math.Clamp(p.Brake * 100, 0, 100);

            // ACC отдаёт руль то в диапазоне −1…1, то в градусах — определяем по размаху данных
            var range = _steerNormalized ? 1.0 : Math.Max(40.0, _steerMaxAbs);
            SteerText = _steerNormalized ? $"{p.Steer * 100:+0;-0;0}%" : $"{p.Steer:+0.0;-0.0;0.0}°";
            var pct = Math.Clamp(p.Steer / range * 100, -100, 100);
            SteerLeftPercent = pct < 0 ? -pct : 0;
            SteerRightPercent = pct > 0 ? pct : 0;

            TelemetryRateText = $"{Telemetry.Rate():F0} Гц";
            TelemetryStatus = "● Запись идёт";
        }
        else
        {
            GasText = "—";
            BrakeText = "—";
            SteerText = "—";
            GasPercent = 0;
            BrakePercent = 0;
            SteerLeftPercent = 0;
            SteerRightPercent = 0;
            TelemetryRateText = "—";
            TelemetryStatus = Telemetry.Points.Count == 0
                ? "Нет данных — запустите агент (вкладка «Агент») и выберите пилота"
                : "Данные не поступают (агент остановлен или ACC закрыт)";
        }
        UpdateTelemetryStats();
    }

    private bool _steerNormalized = true;
    private double _steerMaxAbs;

    private void UpdateTelemetryStats()
    {
        if (Telemetry.Points.Count == 0)
        {
            TelemetryAvgGas = "0%";
            TelemetryAvgBrake = "0%";
            TelemetryMaxSteer = "0";
            TelemetryBrakeTime = "0%";
            TelemetryFullThrottleTime = "0%";
            return;
        }

        double sumGas = 0, sumBrake = 0, maxSteer = 0, brakeTime = 0, fullThrottleTime = 0;
        for (int i = 0; i < Telemetry.Points.Count; i++)
        {
            var p = Telemetry.Points[i];
            sumGas += p.Gas;
            sumBrake += p.Brake;
            maxSteer = Math.Max(maxSteer, Math.Abs(p.Steer));
            if (p.Brake > 0.1) brakeTime++;
            if (p.Gas > 0.9) fullThrottleTime++;
        }

        _steerMaxAbs = maxSteer;
        _steerNormalized = maxSteer <= 1.5;

        TelemetryAvgGas = $"{sumGas / Telemetry.Points.Count * 100:F0}%";
        TelemetryAvgBrake = $"{sumBrake / Telemetry.Points.Count * 100:F0}%";
        TelemetryMaxSteer = _steerNormalized ? $"{maxSteer * 100:F0}%" : $"{maxSteer:F1}°";
        TelemetryBrakeTime = $"{brakeTime / Telemetry.Points.Count * 100:F0}%";
        TelemetryFullThrottleTime = $"{fullThrottleTime / Telemetry.Points.Count * 100:F0}%";
    }

    /// <summary>Единицы руля: true — значения в диапазоне −1…1 (доли хода), false — градусы.</summary>
    public bool SteerNormalized => _steerNormalized;

    private void ClearAutoSave()
    {
        _autoSaveService?.ClearSave();
        ConnectionStatus = "Автосохранение очищено";
    }

    private void ClearAllData()
    {
        _lastSnapshot = null;
        _lastOutline.Clear();
        _lastDots.Clear();
        _recordedOutline = null;
        Telemetry.Clear();
        Standings.Clear();
        Stints.Clear();
        _stintById.Clear();

        SessionText = "—";
        PhaseText = "—";
        TimeText = "—";
        OurDriverText = "—";
        TelemetryDriverName = "—";
        TelemetryStatus = "Ожидание данных от агента…";
        TelemetryRateText = "—";
        GasText = "0%";
        BrakeText = "0%";
        SteerText = "0";
        GasPercent = BrakePercent = SteerLeftPercent = SteerRightPercent = 0;
        _rows.Clear();
        _gapHistory.Clear();
        _radarPrev.Clear();
        SelectedStanding = null;
        RadarNear.Clear();
        RadarFrameData = null;
        RadarUpdated?.Invoke();
        _hasClock = false;
        RemainingText = "--:--:--";
        ElapsedText = "";
        ClockNote = "";
        MapTrackName = "";
        WeatherAir = "";
        WeatherTrack = "";
        WeatherRain = "";
        WeatherClouds = "";
        WeatherWetness = "";

        MapUpdated?.Invoke();
    }

    private async Task ExportToJsonAsync()
    {
        if (_lastSnapshot == null || _exportService == null)
        {
            ConnectionStatus = "Нет данных для экспорта";
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"acc-session_{timestamp}.json";
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                fileName);

            var success = await _exportService.ExportToJsonAsync(_lastSnapshot, downloadsPath);
            ConnectionStatus = success
                ? $"Экспорт выполнен: {fileName}"
                : "Ошибка экспорта JSON";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private async Task ExportToCsvAsync()
    {
        if (_lastSnapshot == null || _exportService == null)
        {
            ConnectionStatus = "Нет данных для экспорта";
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var folderName = $"acc-session_{timestamp}";
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                folderName);

            var success = await _exportService.ExportToCsvAsync(_lastSnapshot, downloadsPath);
            ConnectionStatus = success
                ? $"Экспорт выполнен: {folderName}"
                : "Ошибка экспорта CSV";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Ошибка экспорта: {ex.Message}";
        }
    }

    // Часы:минуты:секунды без переполнения на 24 часах («hh» в TimeSpan показывает только 0–23 часа).
    private static string FormatClock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string FormatMs(int? ms) => ms is int v ? TimeSpan.FromMilliseconds(v).ToString(@"m\:ss\.fff") : "—";
    private static string FormatDuration(DateTime end, DateTime start) => (end - start).ToString(@"h\:mm\:ss");

    private bool _settingsLoaded;

    /// <summary>
    /// Останавливает дочерние процессы при закрытии клиента, чтобы Server не оставался
    /// висеть в фоне и не занимал порт 5261.
    /// </summary>
    public void Shutdown()
    {
        try
        {
            _embeddedAgent?.StopAsync(stopStaleProcesses: true).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DebugLog.Write("Shutdown agent failed: " + ex.Message);
        }

        try
        {
            _embeddedServer?.StopAsync(stopStaleProcesses: true).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DebugLog.Write("Shutdown server failed: " + ex.Message);
        }

        _autoSaveService?.Dispose();
    }

    private void SaveSettings()
    {
        if (!_settingsLoaded) return;
        _settingsService?.SaveSettings(new Services.AppSettings
        {
            OurCarNumber = OurCarNumber,
            RadarMirror = RadarMirror,
            RadarRangeIndex = RadarRangeIndex,
            AgentUseFakeData = AgentUseFakeData,
        });
    }

    partial void OnOurCarNumberChanged(int value) => SaveSettings();

    partial void OnAgentUseFakeDataChanged(bool value) => SaveSettings();
}

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
        new NavItem { Code = "P", Title = "Позиции", Accent = SolidColorBrush.Parse("#2EBD93") },
        new NavItem { Code = "S", Title = "Стинты", Accent = SolidColorBrush.Parse("#4FC3F7") },
        new NavItem { Code = "A", Title = "Сетап", Accent = SolidColorBrush.Parse("#FFB74D") },
        new NavItem { Code = "M", Title = "Карта", Accent = SolidColorBrush.Parse("#E84855") },
        new NavItem { Code = "SV", Title = "Сервер", Accent = SolidColorBrush.Parse("#BA68C8") },
        new NavItem { Code = "AG", Title = "Агент", Accent = SolidColorBrush.Parse("#F06292") },
        new NavItem { Code = "⚙", Title = "Настройки", Accent = SolidColorBrush.Parse("#78909C") },
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
    [ObservableProperty] private string _serverLogs = "";
    [ObservableProperty] private string _agentLogs = "";
    [ObservableProperty] private int _selectedDriverIndex;
    [ObservableProperty] private string _serverExePath = "";
    [ObservableProperty] private string _agentExePath = "";

    public ObservableCollection<string> ServerLogLines { get; } = new();
    public ObservableCollection<string> AgentLogLines { get; } = new();

    private readonly Services.EmbeddedServerService? _embeddedServer;
    private readonly Services.EmbeddedAgentService? _embeddedAgent;
    private readonly Services.ManualUpdateService? _updateService;

    // Update UI
    [ObservableProperty] private string _currentVersionText = "";
    [ObservableProperty] private string _latestVersionText = "";
    [ObservableProperty] private string _updateStatus = "Нажмите кнопку для проверки обновлений";
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private int _updateProgress;
    private object? _pendingUpdate;

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

        // Попытка найти Server и Agent рядом с клиентом
        TryLocateServerAndAgent();

        try
        {
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => { });
            _embeddedServer = new Services.EmbeddedServerService(
                loggerFactory.CreateLogger(typeof(Services.EmbeddedServerService).FullName ?? "EmbeddedServer"));
            _embeddedServer.OnLogLine += line =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ServerLogLines.Add(line);
                    if (ServerLogLines.Count > 500) ServerLogLines.RemoveAt(0);
                    ServerLogs = string.Join("\n", ServerLogLines);
                });
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
                Dispatcher.UIThread.Post(() =>
                {
                    AgentLogLines.Add(line);
                    if (AgentLogLines.Count > 500) AgentLogLines.RemoveAt(0);
                    AgentLogs = string.Join("\n", AgentLogLines);
                });
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
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Failed to init embedded services: {ex}");
        }
    }

    private void TryLocateServerAndAgent()
    {
        var appDir = AppContext.BaseDirectory;
        var serverCandidates = new[]
        {
            Path.Combine(appDir, "Server", "AccEndurance.Server.exe"),
            Path.Combine(Path.GetDirectoryName(appDir) ?? "", "Server", "AccEndurance.Server.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ACC-Endurance-win-x64", "Server", "AccEndurance.Server.exe"),
        };

        var agentCandidates = new[]
        {
            Path.Combine(appDir, "Agent", "AccEndurance.Agent.exe"),
            Path.Combine(Path.GetDirectoryName(appDir) ?? "", "Agent", "AccEndurance.Agent.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ACC-Endurance-win-x64", "Agent", "AccEndurance.Agent.exe"),
        };

        ServerExePath = serverCandidates.FirstOrDefault(File.Exists) ?? "";
        AgentExePath = agentCandidates.FirstOrDefault(File.Exists) ?? "";
    }

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
        await (_embeddedServer?.StopAsync() ?? Task.CompletedTask);
    }

    private async Task StartAgentAsync()
    {
        if (string.IsNullOrEmpty(AgentExePath))
        {
            AgentLogLines.Add("❌ Путь к AccEndurance.Agent.exe не указан");
            return;
        }
        await (_embeddedAgent?.StartAsync(AgentExePath, ServerUrl, SelectedDriverIndex) ?? Task.CompletedTask);
    }

    private async Task StopAgentAsync()
    {
        await (_embeddedAgent?.StopAsync() ?? Task.CompletedTask);
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
            _api = new ApiClient(_baseUrl);
            var initial = await _api.GetSnapshotAsync();
            if (initial == null)
            {
                ConnectionStatus = "Нет ответа от сервера";
                return false;
            }

            // Старое соединение закрываем: иначе при повторном подключении обработчики дублировались
            // и соединение утекало.
            var old = _hub;
            _hub = null;
            if (old != null)
            {
                try { await old.DisposeAsync(); } catch { }
            }

            hub = new HubClient(_baseUrl);
            hub.OnSnapshot(snapshot => Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot)));
            hub.OnOutlineRecorded(points => Dispatcher.UIThread.Post(() => ApplyRecordedOutline(points)));
            hub.ConnectionChanged += (ok, text) => Dispatcher.UIThread.Post(() =>
            {
                IsConnected = ok;
                ConnectionStatus = text;
            });
            await hub.ConnectAsync();

            // «Подключено» выставляем только если SignalR реально поднялся
            // (раньше статус перезаписывался на «Подключено» даже после ошибки SignalR).
            _hub = hub;
            IsConnected = true;
            ConnectionStatus = "Подключено";
            ApplySnapshot(initial);
            _ = LoadSetupsAsync();
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
            return false;
        }
    }

    private void ApplySnapshot(SessionSnapshotDto snapshot)
    {
        _lastSnapshot = snapshot;
        DebugLog.Write($"snapshot: cars={snapshot.Cars.Count} stints={snapshot.Stints.Count} cur={snapshot.CurrentStint?.Driver}");
        try
        {
            var rt = snapshot.Realtime;
        _currentTrackName = rt.TrackName ?? "";
        MapTrackName = _currentTrackName;

        SessionText = $"{rt.SessionType} · {rt.TrackName ?? "трек"}";
        PhaseText = rt.Phase;
        if (rt.SessionEndTime >= rt.SessionTime)
            TimeText = $"{rt.SessionEndTime - rt.SessionTime:hh\\:mm\\:ss} осталось";

        WeatherAir = $"{rt.AmbientTemp}°C";
        WeatherTrack = $"{rt.TrackTemp}°C";
        WeatherRain = $"{rt.RainLevel * 100:F0}%";
        WeatherClouds = $"{rt.Clouds * 100:F0}%";
        WeatherWetness = $"{rt.Wetness * 100:F0}%";

        UpdateStandings(snapshot.Cars);
        UpdateStints(snapshot);
        UpdateMap(snapshot);

        var our = snapshot.Cars.FirstOrDefault(c => c.IsOurCar);
        OurDriverText = our != null ? $"{our.RaceNumber} · {our.Driver} · P{our.Position} · {our.Laps} laps" : "—";

        LastUpdate = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            DebugLog.Write("ApplySnapshot ERROR: " + ex);
        }
    }

    private void UpdateStandings(List<CarStateDto> cars)
    {
        Standings.Clear();
        var idx = 0;
        foreach (var c in cars.OrderBy(c => c.Position))
        {
            var alt = idx++ % 2 == 1;
            var isOur = c.IsOurCar;
            Standings.Add(new StandingsRow
            {
                Position = c.Position,
                RaceNumber = c.RaceNumber,
                TeamName = c.TeamName,
                Driver = c.Driver,
                CarModel = c.CarModelName,
                Laps = c.Laps,
                Gap = c.GapSeconds is double g and >= 0 ? $"{(c.Position == 1 ? "" : "+")}{g:F1}s" : "",
                LastLap = FormatMs(c.LastLapMs),
                BestLap = FormatMs(c.BestLapMs),
                IsOurCar = isOur,
                IsAlt = alt,
                RowBackground = isOur ? new SolidColorBrush(Color.FromRgb(26, 58, 51))
                              : alt ? new SolidColorBrush(Color.FromRgb(28, 34, 44))
                              : new SolidColorBrush(Color.FromRgb(34, 40, 51)),
                RowBorderBrush = isOur ? new SolidColorBrush(Color.FromRgb(46, 189, 147))
                               : new SolidColorBrush(Color.FromRgb(44, 51, 63)),
                RowBorder = isOur ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, 0, 1),
                DriverBrush = isOur ? new SolidColorBrush(Color.FromRgb(46, 189, 147)) : Brushes.White,
            });
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
                StartGame = s.StartSessionTime.ToString(@"hh\:mm\:ss"),
                EndReal = s.EndedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—",
                EndGame = s.EndSessionTime?.ToString(@"hh\:mm\:ss") ?? "—",
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
                StartGame = snapshot.CurrentStint.StartSessionTime.ToString(@"hh\:mm\:ss"),
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
    }

    private static string FormatMs(int? ms) => ms is int v ? TimeSpan.FromMilliseconds(v).ToString(@"m\:ss\.fff") : "—";
    private static string FormatDuration(DateTime end, DateTime start) => (end - start).ToString(@"h\:mm\:ss");
}
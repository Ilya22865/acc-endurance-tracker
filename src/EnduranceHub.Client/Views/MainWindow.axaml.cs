using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using EnduranceHub.Client.Diagnostics;
using EnduranceHub.Client.ViewModels;

namespace EnduranceHub.Client.Views;

public partial class MainWindow : Window
{
    private readonly Canvas? _mapCanvas;
    private readonly Canvas? _radarCanvas;
    private readonly Canvas? _gapTrendCanvas;

    private readonly TelemetryChartRenderer? _gasChart;
    private readonly TelemetryChartRenderer? _brakeChart;
    private readonly TelemetryChartRenderer? _steerChart;

    private readonly ListBox? _navList;
    private readonly Panel? _tourOverlay;
    private readonly Border? _tourCard;
    private readonly Rectangle? _tourHighlight;
    private readonly Rectangle? _tourDimTop;
    private readonly Rectangle? _tourDimBottom;
    private readonly Rectangle? _tourDimLeft;
    private readonly Rectangle? _tourDimRight;
    private Button? _tourNextButton;
    private bool _tourFocused;
    private string _lastTourLayout = "";
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            ApplyWindowsChrome();
            if (DataContext is MainViewModel vm)
            {
                vm.ConnectCommand.Execute(null);

                // Тур запускаем после первой раскладки: до неё у элементов нулевые границы
                // и подсветить кнопку не получится.
                Dispatcher.UIThread.Post(() =>
                {
                    vm.TryStartTour();
                    UpdateTourOverlay();
                }, DispatcherPriority.Loaded);
            }
        };

        _navList = this.FindControl<ListBox>("NavList");
        _tourOverlay = this.FindControl<Panel>("TourOverlay");
        _tourCard = this.FindControl<Border>("TourCard");
        _tourHighlight = this.FindControl<Rectangle>("TourHighlight");
        _tourDimTop = this.FindControl<Rectangle>("TourDimTop");
        _tourDimBottom = this.FindControl<Rectangle>("TourDimBottom");
        _tourDimLeft = this.FindControl<Rectangle>("TourDimLeft");
        _tourDimRight = this.FindControl<Rectangle>("TourDimRight");
        _tourNextButton = this.FindControl<Button>("TourNextButton");

        // Туннельный обработчик: ListBox и TextBox перехватывают стрелки, поэтому
        // на уровне окна в обычной (bubbling) фазе до тура они не доходят.
        AddHandler(KeyDownEvent, OnTourKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        SizeChanged += (_, _) => UpdateTourOverlay();

        _mapCanvas = this.FindControl<Canvas>("MapCanvas");
        _radarCanvas = this.FindControl<Canvas>("RadarCanvas");
        _gapTrendCanvas = this.FindControl<Canvas>("GapTrendCanvas");

        if (_mapCanvas != null) _mapCanvas.SizeChanged += (_, _) => RenderMap();
        if (_radarCanvas != null) _radarCanvas.SizeChanged += (_, _) => RenderRadar();
        if (_gapTrendCanvas != null) _gapTrendCanvas.SizeChanged += (_, _) => RenderGapTrend();

        var gas = this.FindControl<Canvas>("GasCanvas");
        var brake = this.FindControl<Canvas>("BrakeCanvas");
        var steer = this.FindControl<Canvas>("SteerCanvas");
        // Тормоз рисуется на отдельном прозрачном слое поверх газа (без своей сетки).
        // Раньше BrakeCanvas в разметке не было вообще, поэтому график тормоза не отображался.
        if (gas != null) _gasChart = new TelemetryChartRenderer(gas, Palette.Good, 0, 1, percent: true, autoScaleSymmetric: false);
        if (brake != null) _brakeChart = new TelemetryChartRenderer(brake, Palette.Danger, 0, 1, percent: true, autoScaleSymmetric: false, showGrid: false);
        if (steer != null) _steerChart = new TelemetryChartRenderer(steer, Palette.Info, -1, 1, percent: false, autoScaleSymmetric: true);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) Start(vm);
        };
    }

    private void Start(MainViewModel vm)
    {
        if (_started) return;
        _started = true;

        vm.MapUpdated += () => Dispatcher.UIThread.Post(RenderMap);
        vm.RadarUpdated += () => Dispatcher.UIThread.Post(RenderRadar);
        vm.GapTrendChanged += () => Dispatcher.UIThread.Post(RenderGapTrend);
        vm.TourStepChanged += () => Dispatcher.UIThread.Post(UpdateTourOverlay, DispatcherPriority.Loaded);

        // Логи Server/Agent — виртуализированные списки. Держим прокрутку внизу,
        // чтобы новые строки были видны без ручного скролла.
        var serverLog = this.FindControl<ListBox>("ServerLogList");
        var agentLog = this.FindControl<ListBox>("AgentLogList");
        if (serverLog != null) AutoScrollLog(serverLog, vm.ServerLogLines);
        if (agentLog != null) AutoScrollLog(agentLog, vm.AgentLogLines);

        // При открытии вкладки сразу показываем последние строки: пока вкладка была
        // скрыта, автоскролл не тратил время на невидимый список.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPage6))
                Dispatcher.UIThread.Post(() => ScrollLogToEnd(serverLog), DispatcherPriority.Background);
            else if (e.PropertyName == nameof(MainViewModel.IsPage7))
                Dispatcher.UIThread.Post(() => ScrollLogToEnd(agentLog), DispatcherPriority.Background);
        };

        // графики телеметрии: 60 кадров в секунду, только пока открыта страница.
        // Рисуем не «по приходу пачки», а каждый кадр — тогда линия движется непрерывно.
        var telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var tick = 0;
        telemetryTimer.Tick += (_, _) =>
        {
            if (!vm.IsPage2) return;
            RenderTelemetry(vm);
            if (++tick % 6 == 0) vm.RefreshTelemetryTexts();
        };
        telemetryTimer.Start();

        // Радар тоже перерисовываем по кадровому таймеру, а не только по снимкам
        // сервера (они приходят ~10 раз в секунду). Позиции машин интерполируются.
        var radarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        radarTimer.Tick += (_, _) =>
        {
            if (vm.IsPage3) RenderRadar();
        };
        radarTimer.Start();

        // обратный отсчёт идёт плавно между снимками сервера
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clockTimer.Tick += (_, _) => vm.TickClock();
        clockTimer.Start();
    }

    private static void AutoScrollLog(ListBox list, System.Collections.Specialized.INotifyCollectionChanged lines)
    {
        lines.CollectionChanged += (_, e) =>
        {
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add) return;
            Dispatcher.UIThread.Post(() => ScrollLogToEnd(list), DispatcherPriority.Background);
        };
    }

    private static void ScrollLogToEnd(ListBox? list)
    {
        if (list == null || !list.IsEffectivelyVisible || list.ItemCount == 0) return;
        list.ScrollIntoView(list.ItemCount - 1);
    }

    private void RenderTelemetry(MainViewModel vm)
    {
        // Небольшая задержка (0.12 с) скрывает сетевой джиттер. Данные приходят
        // пачками каждые ~50 мс, а рендерер интерполирует крайние точки,
        // поэтому линия доходит до правого края непрерывно, без рывков.
        var now = vm.Telemetry.Now - 0.12;
        var window = vm.TelemetryWindowSeconds;
        var points = vm.Telemetry.Points;

        _gasChart?.Render(points, now, window, p => p.Gas);
        _brakeChart?.Render(points, now, window, p => p.Brake);
        _steerChart?.Render(points, now, window, p => p.Steer);
    }

    private void RenderRadar()
    {
        if (_radarCanvas == null || DataContext is not MainViewModel vm) return;
        if (!vm.IsPage3) return;
        // GetRadarFrameSmooth интерполирует позиции между двумя последними снимками,
        // поэтому машины двигаются плавно на 30 кадрах/с, а не прыгают 10 раз в секунду.
        RadarRenderer.Render(_radarCanvas, vm.GetRadarFrameSmooth(), vm.RadarRangeMeters);
    }

    private void RenderGapTrend()
    {
        if (_gapTrendCanvas == null || DataContext is not MainViewModel vm) return;
        var row = vm.SelectedStanding;
        if (row == null || !vm.IsPage0) return;
        GapTrendRenderer.Render(_gapTrendCanvas, vm.GetGapHistory(row.CarIndex), vm.HistoryNow);
    }

    // ===================== ОБУЧЕНИЕ =====================

    /// <summary>
    /// Раскладывает затемнение, подсветку и карточку по реальным границам элемента.
    /// Четыре прямоугольника вокруг цели дают «окно» без масок и композитинга.
    /// </summary>
    private void UpdateTourOverlay()
    {
        if (_tourOverlay == null) return;

        if (DataContext is not MainViewModel vm || !vm.IsTourVisible)
        {
            _tourOverlay.IsVisible = false;
            _tourFocused = false;
            return;
        }

        _tourOverlay.IsVisible = true;

        var width = _tourOverlay.Bounds.Width;
        var height = _tourOverlay.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var target = GetTourTargetRect(vm);

        if (target is Rect rect)
        {
            SetDim(_tourDimTop, 0, 0, width, rect.Y);
            SetDim(_tourDimBottom, 0, rect.Bottom, width, height - rect.Bottom);
            SetDim(_tourDimLeft, 0, rect.Y, rect.X, rect.Height);
            SetDim(_tourDimRight, rect.Right, rect.Y, width - rect.Right, rect.Height);

            if (_tourHighlight != null)
            {
                _tourHighlight.IsVisible = true;
                _tourHighlight.Width = rect.Width + 8;
                _tourHighlight.Height = rect.Height + 8;
                Canvas.SetLeft(_tourHighlight, rect.X - 4);
                Canvas.SetTop(_tourHighlight, rect.Y - 4);
            }
        }
        else
        {
            SetDim(_tourDimTop, 0, 0, width, height);
            SetDim(_tourDimBottom, 0, 0, 0, 0);
            SetDim(_tourDimLeft, 0, 0, 0, 0);
            SetDim(_tourDimRight, 0, 0, 0, 0);
            if (_tourHighlight != null) _tourHighlight.IsVisible = false;
        }

        PositionTourCard(width, height, target);

        // Фокус на «Далее»: Enter/Space работают сразу, а стрелки не уходят в ListBox.
        if (!_tourFocused)
        {
            _tourFocused = true;
            _tourNextButton?.Focus();
        }
    }

    private static void SetDim(Rectangle? rectangle, double x, double y, double width, double height)
    {
        if (rectangle == null) return;

        if (width <= 0 || height <= 0)
        {
            rectangle.IsVisible = false;
            return;
        }

        rectangle.IsVisible = true;
        rectangle.Width = width;
        rectangle.Height = height;
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
    }

    private void PositionTourCard(double width, double height, Rect? target)
    {
        if (_tourCard == null) return;

        const double margin = 16;
        _tourCard.Measure(new Size(_tourCard.Width, double.PositiveInfinity));
        var cardWidth = _tourCard.DesiredSize.Width;
        var cardHeight = _tourCard.DesiredSize.Height;

        double x;
        double y;

        if (target is Rect rect)
        {
            // Справа от цели, если хватает места, иначе слева, иначе прижимаем к краю.
            if (rect.Right + margin + cardWidth <= width)
                x = rect.Right + margin;
            else if (rect.X - margin - cardWidth >= 0)
                x = rect.X - margin - cardWidth;
            else
                x = rect.X;

            y = rect.Y + rect.Height / 2 - cardHeight / 2;
        }
        else
        {
            x = (width - cardWidth) / 2;
            y = (height - cardHeight) / 2;
        }

        x = Math.Clamp(x, margin, Math.Max(margin, width - cardWidth - margin));
        y = Math.Clamp(y, margin, Math.Max(margin, height - cardHeight - margin));

        Canvas.SetLeft(_tourCard, x);
        Canvas.SetTop(_tourCard, y);

        if (DataContext is MainViewModel vm)
        {
            var signature = $"{vm.TourCounterText}|{target}|{x:0}|{y:0}|{width:0}x{height:0}";
            if (signature != _lastTourLayout)
            {
                _lastTourLayout = signature;
                DebugLog.Write($"Tour {vm.TourCounterText}: target={target?.ToString() ?? "none"} card={x:0},{y:0}");
            }
        }
    }

    /// <summary>Границы подсвечиваемого элемента в координатах оверлея.</summary>
    private Rect? GetTourTargetRect(MainViewModel vm)
    {
        if (_tourOverlay == null) return null;

        var step = vm.CurrentTourStep;
        if (step?.Target == null) return null;

        Control? target = step.Target switch
        {
            "nav" when step.NavIndex is int index => GetNavItem(index),
            "connect" => this.FindControl<Control>("ConnectPanel"),
            _ => this.FindControl<Control>(step.Target),
        };

        if (target == null) return null;

        var origin = target.TranslatePoint(new Point(0, 0), _tourOverlay);
        if (origin == null) return null;

        var size = target.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return null;

        return new Rect(origin.Value, size);
    }

    private Control? GetNavItem(int index)
    {
        // Пункт навигации может быть ещё не создан (виртуализация) — тогда
        // подсвечиваем весь список, это лучше, чем ничего.
        var container = _navList?.ContainerFromIndex(index);
        return container ?? _navList;
    }

    private void OnTourKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsTourVisible) return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.SkipTourCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                vm.PreviousTourStepCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.NextTourStepCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Красит системный заголовок окна в цвета приложения: иначе Windows рисует его
    /// акцентом системы (у пользователя он может быть ярко-фиолетовым) и строгая
    /// графитовая палитра разваливается. На Windows 10 вызов просто ничего не делает.
    /// </summary>
    private void ApplyWindowsChrome()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        try
        {
            // COLORREF = 0x00BBGGRR
            int caption = 0x00191411; // #111419 — SurfaceBrush
            int border = 0x00302823;  // #232830 — LineBrush
            int text = 0x00F5F2F0;    // #F0F2F5 — TextBrush
            int corner = 2;           // DWMWCP_ROUND

            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        }
        catch
        {
            // DWM есть не во всех сборках Windows — оформление не критично для работы.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void RenderMap()
    {
        if (_mapCanvas == null || DataContext is not MainViewModel vm) return;
        var w = _mapCanvas.Bounds.Width;
        var h = _mapCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _mapCanvas.Children.Clear();
        var outline = vm.CurrentOutline;

        if (outline.Count < 2)
        {
            _mapCanvas.Children.Add(new TextBlock
            {
                Text = "Нет данных о трассе",
                Foreground = new SolidColorBrush(Palette.Text3),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            return;
        }

        var minX = outline.Min(p => p.X);
        var maxX = outline.Max(p => p.X);
        var minY = outline.Min(p => p.Y);
        var maxY = outline.Max(p => p.Y);
        var extW = Math.Max(maxX - minX, 1e-6);
        var extH = Math.Max(maxY - minY, 1e-6);
        const double pad = 36;
        var scale = Math.Min((w - pad * 2) / extW, (h - pad * 2) / extH);
        var offsetX = (w - extW * scale) / 2 - minX * scale;
        var offsetY = (h - extH * scale) / 2 - minY * scale;

        var points = new Points();
        foreach (var p in outline)
            points.Add(new Point(minX == maxX ? w / 2 : p.X * scale + offsetX,
                                 minY == maxY ? h / 2 : p.Y * scale + offsetY));

        // полотно трассы: широкая подложка и более светлая «асфальтовая» линия
        _mapCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Palette.Surface3),
            StrokeThickness = 16,
            StrokeJoin = PenLineJoin.Round,
        });
        var inner = new Points();
        foreach (var p in points) inner.Add(p);
        _mapCanvas.Children.Add(new Polyline
        {
            Points = inner,
            Stroke = new SolidColorBrush(Palette.LineStrong),
            StrokeThickness = 10,
            StrokeJoin = PenLineJoin.Round,
        });

        var start = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Palette.Text),
            Stroke = new SolidColorBrush(Palette.Bg),
            StrokeThickness = 2,
        };
        Canvas.SetLeft(start, points[0].X - 6);
        Canvas.SetTop(start, points[0].Y - 6);
        _mapCanvas.Children.Add(start);

        // сначала чужие машины, нашу — последней, чтобы она была сверху
        foreach (var dot in vm.CurrentDots.OrderBy(d => d.IsOurCar ? 1 : 0))
        {
            var p = new Point(dot.X * scale + offsetX, dot.Y * scale + offsetY);
            var isOur = dot.IsOurCar;
            var r = isOur ? 9.0 : 7.0;

            var car = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = new SolidColorBrush(isOur ? Palette.Own : Palette.Surface3),
                Stroke = new SolidColorBrush(isOur ? Palette.Accent : Palette.Text3),
                StrokeThickness = isOur ? 2 : 1,
            };
            Canvas.SetLeft(car, p.X - r);
            Canvas.SetTop(car, p.Y - r);
            _mapCanvas.Children.Add(car);

            var label = new TextBlock
            {
                Text = dot.RaceNumber.ToString(),
                FontSize = isOur ? 11 : 9,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(isOur ? Palette.OnOwn : Palette.Text),
                Width = r * 2 + 8,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(label, p.X - r - 4);
            Canvas.SetTop(label, p.Y - (isOur ? 7.5 : 6.5));
            _mapCanvas.Children.Add(label);
        }
    }
}

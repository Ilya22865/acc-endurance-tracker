using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using AccEndurance.Client.ViewModels;

namespace AccEndurance.Client.Views;

public partial class MainWindow : Window
{
    private readonly Canvas? _mapCanvas;
    private readonly Canvas? _radarCanvas;
    private readonly Canvas? _gapTrendCanvas;

    private readonly TelemetryChartRenderer? _gasChart;
    private readonly TelemetryChartRenderer? _brakeChart;
    private readonly TelemetryChartRenderer? _steerChart;
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as MainViewModel)?.ConnectCommand.Execute(null);

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
                Stroke = new SolidColorBrush(isOur ? Palette.Text : Palette.Text3),
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
                Foreground = new SolidColorBrush(isOur ? Palette.Bg : Palette.Text),
                Width = r * 2 + 8,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(label, p.X - r - 4);
            Canvas.SetTop(label, p.Y - (isOur ? 7.5 : 6.5));
            _mapCanvas.Children.Add(label);
        }
    }
}

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AccEndurance.Client.Views;

public partial class MainWindow : Window
{
    private Canvas? _mapCanvas;
    private Canvas? _radarCanvas;
    private TextBlock? _radarStatus;

    private TelemetryChartRenderer? _gasChart;
    private TelemetryChartRenderer? _brakeChart;
    private TelemetryChartRenderer? _steerChart;
    private bool _timersStarted;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as ViewModels.MainViewModel)?.ConnectCommand.Execute(null);

        _mapCanvas = this.FindControl<Canvas>("MapCanvas");
        if (_mapCanvas != null)
        {
            _mapCanvas.SizeChanged += (_, _) => RenderMap();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is ViewModels.MainViewModel vm)
                    vm.MapUpdated += () => Dispatcher.UIThread.Post(RenderMap);
            };
        }

        _radarCanvas = this.FindControl<Canvas>("RadarCanvas");
        _radarStatus = this.FindControl<TextBlock>("RadarStatus");

        var gas = this.FindControl<Canvas>("GasCanvas");
        var brake = this.FindControl<Canvas>("BrakeCanvas");
        var steer = this.FindControl<Canvas>("SteerCanvas");
        if (gas != null) _gasChart = new TelemetryChartRenderer(gas, Color.FromRgb(0x2E, 0xBD, 0x93), 0, 1, percent: true, autoScaleSymmetric: false);
        if (brake != null) _brakeChart = new TelemetryChartRenderer(brake, Color.FromRgb(0xE8, 0x48, 0x55), 0, 1, percent: true, autoScaleSymmetric: false);
        if (steer != null) _steerChart = new TelemetryChartRenderer(steer, Color.FromRgb(0x4F, 0xC3, 0xF7), -1, 1, percent: false, autoScaleSymmetric: true);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm) StartTimers(vm);
        };
    }

    private void StartTimers(ViewModels.MainViewModel vm)
    {
        if (_timersStarted) return;
        _timersStarted = true;

        // графики телеметрии: ~30 кадров в секунду, только пока открыта страница
        var telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        var tick = 0;
        telemetryTimer.Tick += (_, _) =>
        {
            if (!vm.IsPage2) return;
            RenderTelemetry(vm);
            if (++tick % 3 == 0) vm.RefreshTelemetryTexts();
        };
        telemetryTimer.Start();

        var radarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        radarTimer.Tick += (_, _) =>
        {
            if (vm.IsPage3) RenderRadar(vm);
        };
        radarTimer.Start();
    }

    private void RenderTelemetry(ViewModels.MainViewModel vm)
    {
        // рисуем с небольшой задержкой (0.3 с): данные приходят пачками, так линия движется плавно
        var now = vm.Telemetry.Now - 0.3;
        var window = vm.TelemetryWindowSeconds;
        var points = vm.Telemetry.Points;

        _gasChart?.Render(points, now, window, p => p.Gas);
        _brakeChart?.Render(points, now, window, p => p.Brake);
        _steerChart?.Render(points, now, window, p => p.Steer);
    }

    private void RenderMap()
    {
        if (_mapCanvas == null || DataContext is not ViewModels.MainViewModel vm) return;
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
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA5)),
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
        const double pad = 30;
        var scale = Math.Min((w - pad * 2) / extW, (h - pad * 2) / extH);
        var offsetX = (w - extW * scale) / 2 - minX * scale;
        var offsetY = (h - extH * scale) / 2 - minY * scale;

        var points = new Points();
        foreach (var p in outline)
            points.Add(new Point(minX == maxX ? w / 2 : p.X * scale + offsetX,
                                 minY == maxY ? h / 2 : p.Y * scale + offsetY));

        _mapCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromRgb(0x5A, 0x66, 0x7A)),
            StrokeThickness = 3,
        });

        var start = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x4A)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C)),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(start, points[0].X - 4);
        Canvas.SetTop(start, points[0].Y - 4);
        _mapCanvas.Children.Add(start);

        foreach (var dot in vm.CurrentDots)
        {
            var p = new Point(dot.X * scale + offsetX, dot.Y * scale + offsetY);
            var isOur = dot.IsOurCar;
            var r = isOur ? 6.0 : 4.0;
            var fill = isOur
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xBD, 0x93))
                : new SolidColorBrush(Color.FromRgb(0xE8, 0x4C, 0x55));

            var car = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = fill,
                Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(car, p.X - r);
            Canvas.SetTop(car, p.Y - r);
            _mapCanvas.Children.Add(car);

            var label = new TextBlock
            {
                Text = $"#{dot.RaceNumber}",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = isOur
                    ? new SolidColorBrush(Color.FromRgb(0x2E, 0xBD, 0x93))
                    : new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xDF)),
            };
            Canvas.SetLeft(label, p.X + r + 2);
            Canvas.SetTop(label, p.Y - 7);
            _mapCanvas.Children.Add(label);
        }
    }

    // --- радар ---
    // Положение соперников считается по реальным мировым координатам машин (метры).
    // Направление «вперёд» определяется по движению нашей машины.
    // Если на радаре лево/право перепутаны — поставьте RadarMirrorX = true.
    private const bool RadarMirrorX = true;
    private const double RadarRadiusPx = 250;
    private const double RadarRangeMeters = 60;
    private const double WarningMeters = 15;

    private double _headX = 0, _headY = 1;
    private double _refX, _refY;
    private bool _hasRef;

    private void RenderRadar(ViewModels.MainViewModel vm)
    {
        if (_radarCanvas == null) return;

        var w = _radarCanvas.Bounds.Width;
        var h = _radarCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _radarCanvas.Children.Clear();

        var cx = w / 2;
        var cy = h / 2;

        // кольца 20/40/60 м
        var ringBrush = new SolidColorBrush(Color.Parse("#2A2F38"));
        var labelBrush = new SolidColorBrush(Color.Parse("#70757A"));
        for (int i = 1; i <= 3; i++)
        {
            var r = RadarRadiusPx * i / 3;
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = ringBrush, StrokeThickness = 1 };
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);
            _radarCanvas.Children.Add(ring);

            var rl = new TextBlock { Text = $"{RadarRangeMeters * i / 3:F0} м", FontSize = 9, Foreground = labelBrush };
            Canvas.SetLeft(rl, cx + 3);
            Canvas.SetTop(rl, cy - r - 12);
            _radarCanvas.Children.Add(rl);
        }

        var axis = new SolidColorBrush(Color.Parse("#2D3139"));
        _radarCanvas.Children.Add(new Line { StartPoint = new Point(cx, cy - RadarRadiusPx), EndPoint = new Point(cx, cy + RadarRadiusPx), Stroke = axis, StrokeThickness = 1 });
        _radarCanvas.Children.Add(new Line { StartPoint = new Point(cx - RadarRadiusPx, cy), EndPoint = new Point(cx + RadarRadiusPx, cy), Stroke = axis, StrokeThickness = 1 });

        var fwd = new TextBlock { Text = "▲ ВПЕРЁД", FontSize = 9, Foreground = labelBrush };
        Canvas.SetLeft(fwd, cx - 22);
        Canvas.SetTop(fwd, 2);
        _radarCanvas.Children.Add(fwd);

        // --- наша машина ---
        // Раньше она искалась по номеру из настроек клиента (по умолчанию 0) и, не найдя, радар
        // выходил, оставив только рамку. Теперь берём машину, которую сервер пометил как нашу.
        var snapshot = vm.LastSnapshot;
        var cars = snapshot?.Cars;
        var our = cars?.FirstOrDefault(c => c.IsOurCar)
                  ?? (vm.OurCarNumber > 0 ? cars?.FirstOrDefault(c => c.RaceNumber == vm.OurCarNumber) : null);

        if (our == null || cars == null)
        {
            SetRadarStatus(snapshot == null
                ? "Нет данных от сервера"
                : "Ваша машина не определена: задайте OurCarRaceNumber на сервере или номер в «Настройки»");
            DrawCenterCar(cx, cy, vm.OurCarNumber > 0 ? vm.OurCarNumber.ToString() : "");
            return;
        }

        // направление движения по смещению нашей машины
        var ox = our.WorldX;
        var oy = our.WorldY;
        if (!_hasRef)
        {
            _refX = ox; _refY = oy; _hasRef = true;
        }
        var mx = ox - _refX;
        var my = oy - _refY;
        var md = Math.Sqrt(mx * mx + my * my);
        if (md >= 3.0)
        {
            if (md < 40.0 && our.Kmh > 5)
            {
                _headX = mx / md;
                _headY = my / md;
            }
            _refX = ox; _refY = oy; // при «телепорте» (питы, рестарт) направление не трогаем
        }

        var near = 0;
        foreach (var c in cars)
        {
            if (c.CarIndex == our.CarIndex) continue;
            if (c.WorldX == 0 && c.WorldY == 0) continue; // позиция ещё не пришла

            var dx = c.WorldX - ox;
            var dy = c.WorldY - oy;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > RadarRangeMeters) continue;

            // в систему координат нашей машины: ry — вперёд, rx — вправо
            var rx = dx * _headY - dy * _headX;
            var ry = dx * _headX + dy * _headY;
            if (RadarMirrorX) rx = -rx;

            var sx = cx + rx / RadarRangeMeters * RadarRadiusPx;
            var sy = cy - ry / RadarRangeMeters * RadarRadiusPx;

            var warn = dist < WarningMeters;
            if (warn) near++;
            var color = warn ? Color.Parse("#FFB74D") : Color.Parse("#9AA0A6");

            // Прямоугольники: 16 в ширину, 8 в высоту
            var dot = new Rectangle { Width = 16, Height = 8, Fill = new SolidColorBrush(color) };
            Canvas.SetLeft(dot, sx - 8);
            Canvas.SetTop(dot, sy - 4);
            _radarCanvas.Children.Add(dot);

            var label = new TextBlock { Text = $"#{c.RaceNumber}", FontSize = 10, Foreground = new SolidColorBrush(color) };
            Canvas.SetLeft(label, sx + 7);
            Canvas.SetTop(label, sy - 8);
            _radarCanvas.Children.Add(label);
        }

        DrawCenterCar(cx, cy, our.RaceNumber.ToString());
        SetRadarStatus(near > 0
            ? $"⚠ Рядом {near} маш. (ближе {WarningMeters:F0} м)"
            : "Ваша машина — синий квадрат в центре. Оранжевые квадраты — соперники ближе 15 м");
    }

    private void DrawCenterCar(double cx, double cy, string number)
    {
        if (_radarCanvas == null) return;
        // Используем прямоугольник для нашей машины
        var me = new Rectangle { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.Parse("#2962FF")) };
        Canvas.SetLeft(me, cx - 6);
        Canvas.SetTop(me, cy - 6);
        _radarCanvas.Children.Add(me);

        if (number.Length == 0) return;
        var lbl = new TextBlock { Text = number, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
        Canvas.SetLeft(lbl, cx + 10);
        Canvas.SetTop(lbl, cy - 8);
        _radarCanvas.Children.Add(lbl);
    }

    private void SetRadarStatus(string text)
    {
        if (_radarStatus != null && _radarStatus.Text != text) _radarStatus.Text = text;
    }
}

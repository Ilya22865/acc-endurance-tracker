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
        if (_radarCanvas != null)
        {
            DataContextChanged += (_, _) =>
            {
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (_, _) => RenderRadar(vm);
                    timer.Start();
                }
            };
        }
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

    private void RenderRadar(ViewModels.MainViewModel vm)
    {
        if (_radarCanvas == null) return;

        var w = _radarCanvas.Bounds.Width;
        var h = _radarCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _radarCanvas.Children.Clear();

        var centerX = w / 2;
        var centerY = h / 2;
        const double RadarRadius = 150;
        const double RadarRange = 100; // meters

        // Draw radar circles
        for (int i = 1; i <= 3; i++)
        {
            var radius = RadarRadius * i / 3;
            var circle = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = new SolidColorBrush(Color.Parse("#1F2229")),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(circle, centerX - radius);
            Canvas.SetTop(circle, centerY - radius);
            _radarCanvas.Children.Add(circle);
        }

        // Draw center lines
        var lineColor = new SolidColorBrush(Color.Parse("#2D3139"));
        var vLine = new Line
        {
            StartPoint = new Point(centerX, centerY - RadarRadius),
            EndPoint = new Point(centerX, centerY + RadarRadius),
            Stroke = lineColor,
            StrokeThickness = 1,
        };
        _radarCanvas.Children.Add(vLine);

        var hLine = new Line
        {
            StartPoint = new Point(centerX - RadarRadius, centerY),
            EndPoint = new Point(centerX + RadarRadius, centerY),
            Stroke = lineColor,
            StrokeThickness = 1,
        };
        _radarCanvas.Children.Add(hLine);

        // Get our car
        var ourCar = vm.Standings.FirstOrDefault(c => c.RaceNumber == vm.OurCarNumber);
        if (ourCar == null) return;

        var ourCarIndex = vm.Standings.IndexOf(ourCar);
        if (ourCarIndex == -1) return;

        // Draw other cars relative to our position
        foreach (var car in vm.Standings)
        {
            if (car.RaceNumber == vm.OurCarNumber) continue;

            var carIndex = vm.Standings.IndexOf(car);
            var relativePosition = carIndex - ourCarIndex;

            // Calculate position on radar (simplified - ahead/behind)
            var distance = Math.Abs(relativePosition) * 20.0; // Approximate distance
            if (distance > RadarRange) continue;

            var angle = relativePosition > 0 ? Math.PI / 2 : -Math.PI / 2; // Ahead = up, Behind = down
            var scale = distance / RadarRange;
            var x = centerX + Math.Sin(angle) * scale * RadarRadius;
            var y = centerY - Math.Cos(angle) * scale * RadarRadius;

            // Draw car dot
            var carDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.Parse("#70757A")),
            };
            Canvas.SetLeft(carDot, x - 4);
            Canvas.SetTop(carDot, y - 4);
            _radarCanvas.Children.Add(carDot);

            // Draw car number
            var carLabel = new TextBlock
            {
                Text = car.RaceNumber.ToString(),
                FontSize = 10,
                Foreground = Brushes.White,
            };
            Canvas.SetLeft(carLabel, x - 8);
            Canvas.SetTop(carLabel, y - 18);
            _radarCanvas.Children.Add(carLabel);
        }

        // Draw our car in center
        var ourCarDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.Parse("#2962FF")),
        };
        Canvas.SetLeft(ourCarDot, centerX - 6);
        Canvas.SetTop(ourCarDot, centerY - 6);
        _radarCanvas.Children.Add(ourCarDot);

        // Draw our car number
        var ourCarLabel = new TextBlock
        {
            Text = vm.OurCarNumber.ToString(),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
        };
        Canvas.SetLeft(ourCarLabel, centerX - 10);
        Canvas.SetTop(ourCarLabel, centerY - 22);
        _radarCanvas.Children.Add(ourCarLabel);

        // Draw range label
        var rangeLabel = new TextBlock
        {
            Text = $"{RadarRange:F0}m",
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.Parse("#70757A")),
        };
        Canvas.SetLeft(rangeLabel, centerX - 15);
        Canvas.SetTop(rangeLabel, 5);
        _radarCanvas.Children.Add(rangeLabel);
    }
}
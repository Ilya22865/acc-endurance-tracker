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
}
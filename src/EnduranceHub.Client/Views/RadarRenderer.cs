using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using EnduranceHub.Client.ViewModels;

namespace EnduranceHub.Client.Views;

/// <summary>
/// Радар вокруг нашей машины: кольца дальности, машины соперников прямоугольниками реального размера
/// (повёрнутыми по их курсу), красная подсветка сбоку, когда рядом идёт машина.
/// Кадр (RadarFrame) считается во вью-модели, здесь только рисование. Вверх — направление движения нашей машины.
/// </summary>
public static class RadarRenderer
{
    private const double CarWidthM = 2.0;
    private const double CarLengthM = 4.7;

    public static void Render(Canvas canvas, RadarFrame? frame, double rangeMeters)
    {
        var w = canvas.Bounds.Width;
        var h = canvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        canvas.Children.Clear();

        var cx = w / 2;
        var cy = h / 2;
        var radius = Math.Min(w, h) / 2 - 16;
        if (radius < 60 || rangeMeters <= 0) return;
        var ppm = radius / rangeMeters; // пикселей на метр

        var ringBrush = new SolidColorBrush(Palette.LineStrong);
        var bandBrush = new SolidColorBrush(Color.FromArgb(0x59, Palette.Surface2.R, Palette.Surface2.G, Palette.Surface2.B));
        var textBrush = new SolidColorBrush(Palette.Text3);

        // кольца: 4 штуки, внешнее рисуем первым
        for (int i = 4; i >= 1; i--)
        {
            var r = radius * i / 4;
            var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = ringBrush, StrokeThickness = 1, Fill = bandBrush };
            Canvas.SetLeft(ring, cx - r);
            Canvas.SetTop(ring, cy - r);
            canvas.Children.Add(ring);

            // подписи по диагонали, чтобы не перекрывать машины спереди и сзади
            var label = new TextBlock { Text = $"{rangeMeters * i / 4:0} м", FontSize = 11, Foreground = textBrush };
            Canvas.SetLeft(label, cx + r * 0.7071 + 4);
            Canvas.SetTop(label, cy - r * 0.7071 - 14);
            canvas.Children.Add(label);
        }

        var axis = new SolidColorBrush(Palette.Line);
        canvas.Children.Add(new Line { StartPoint = new Point(cx, cy - radius), EndPoint = new Point(cx, cy + radius), Stroke = axis, StrokeThickness = 1 });
        canvas.Children.Add(new Line { StartPoint = new Point(cx - radius, cy), EndPoint = new Point(cx + radius, cy), Stroke = axis, StrokeThickness = 1 });

        var carW = Math.Max(10, CarWidthM * ppm);
        var carH = Math.Max(20, CarLengthM * ppm);

        if (frame != null)
        {
            foreach (var car in frame.Cars)
            {
                if (car.Distance > rangeMeters * 1.1) continue;

                var px = cx + car.X * ppm;
                var py = cy - car.Y * ppm;
                var stroke = car.IsDanger ? Palette.Danger : car.IsWarn ? Palette.Warn : Palette.LineStrong;
                var fill = car.IsDanger
                    ? Color.FromArgb(0x70, Palette.Danger.R, Palette.Danger.G, Palette.Danger.B)
                    : car.IsWarn
                        ? Color.FromArgb(0x60, Palette.Warn.R, Palette.Warn.G, Palette.Warn.B)
                        : Palette.Surface3;

                var rect = new Rectangle
                {
                    Width = carW,
                    Height = carH,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = new SolidColorBrush(fill),
                    Stroke = new SolidColorBrush(stroke),
                    StrokeThickness = 1.5,
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    RenderTransform = new RotateTransform(car.AngleDeg),
                };
                Canvas.SetLeft(rect, px - carW / 2);
                Canvas.SetTop(rect, py - carH / 2);
                canvas.Children.Add(rect);

                var num = new TextBlock
                {
                    Text = car.Number.ToString(),
                    Width = 44,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Palette.Text),
                };
                Canvas.SetLeft(num, px - 22);
                Canvas.SetTop(num, py - 9);
                canvas.Children.Add(num);

                var dist = new TextBlock
                {
                    Text = $"{car.Distance:0.0} м",
                    Width = 60,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(car.IsDanger ? Palette.Danger : car.IsWarn ? Palette.Warn : Palette.Text2),
                };
                Canvas.SetLeft(dist, px - 30);
                Canvas.SetTop(dist, py + carH / 2 + 2);
                canvas.Children.Add(dist);
            }

            // красные дуги по бокам нашей машины: рядом идёт соперник
            var spotterPen = new SolidColorBrush(Palette.Danger);
            if (frame.SpotterLeft) AddSpotter(canvas, cx - carW / 2 - 12, cy, carH, spotterPen);
            if (frame.SpotterRight) AddSpotter(canvas, cx + carW / 2 + 12, cy, carH, spotterPen);
        }

        // наша машина — поверх всего
        var me = new Rectangle
        {
            Width = carW,
            Height = carH,
            RadiusX = 5,
            RadiusY = 5,
            Fill = new SolidColorBrush(Palette.Own),
            Stroke = new SolidColorBrush(Palette.Text),
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(me, cx - carW / 2);
        Canvas.SetTop(me, cy - carH / 2);
        canvas.Children.Add(me);

        // «нос» машины
        var nose = new Polygon
        {
            Fill = new SolidColorBrush(Color.Parse("#1B0E09")),
            Points = new Points
            {
                new Point(cx, cy - carH / 2 + 5),
                new Point(cx - 5, cy - carH / 2 + 14),
                new Point(cx + 5, cy - carH / 2 + 14),
            },
        };
        canvas.Children.Add(nose);

        if (frame != null && frame.Number.Length > 0)
        {
            var ownNum = new TextBlock
            {
                Text = frame.Number,
                Width = 44,
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#1B0E09")),
            };
            Canvas.SetLeft(ownNum, cx - 22);
            Canvas.SetTop(ownNum, cy - 3);
            canvas.Children.Add(ownNum);
        }
    }

    private static void AddSpotter(Canvas canvas, double x, double cy, double carH, IBrush brush)
    {
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(x, cy - carH * 0.8),
            EndPoint = new Point(x, cy + carH * 0.8),
            Stroke = brush,
            StrokeThickness = 6,
            StrokeLineCap = PenLineCap.Round,
        });
    }
}

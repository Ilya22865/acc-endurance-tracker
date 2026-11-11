using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace EnduranceHub.Client.Views;

/// <summary>
/// Маленький график «как менялся отрыв до нашей машины». Выше нулевой линии — соперник впереди нас,
/// ниже — позади. Ось X — последние минуты гонки.
/// </summary>
public static class GapTrendRenderer
{
    private const double WindowSeconds = 600;

    public static void Render(Canvas canvas, IReadOnlyList<(double T, double Gap)> history, double now)
    {
        var w = canvas.Bounds.Width;
        var h = canvas.Bounds.Height;
        if (w <= 20 || h <= 20) return;

        canvas.Children.Clear();

        var textBrush = new SolidColorBrush(Palette.Text3);

        if (history.Count < 2)
        {
            var empty = new TextBlock { Text = "Копим данные…", FontSize = 12, Foreground = textBrush };
            Canvas.SetLeft(empty, 10);
            Canvas.SetTop(empty, h / 2 - 8);
            canvas.Children.Add(empty);
            return;
        }

        // окно по времени: последние 10 минут, но не меньше 2 минут
        var tEnd = Math.Max(now, history[history.Count - 1].T);
        var tStart = Math.Max(history[0].T, tEnd - WindowSeconds);
        if (tEnd - tStart < 120) tStart = tEnd - 120;

        double min = 0, max = 0;
        foreach (var p in history)
        {
            if (p.T < tStart) continue;
            min = Math.Min(min, p.Gap);
            max = Math.Max(max, p.Gap);
        }
        if (max - min < 3) { max += 1.5; min -= 1.5; }
        var pad = (max - min) * 0.12;
        max += pad;
        min -= pad;

        const double left = 34, right = 6, top = 6, bottom = 6;
        var pw = w - left - right;
        var ph = h - top - bottom;
        double X(double t) => left + Math.Clamp((t - tStart) / (tEnd - tStart), 0, 1) * pw;
        double Y(double g) => top + (max - g) / (max - min) * ph;

        var zeroY = Y(0);
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(left, zeroY),
            EndPoint = new Point(w - right, zeroY),
            Stroke = new SolidColorBrush(Palette.Own),
            StrokeThickness = 1,
            StrokeDashArray = new AvaloniaList<double> { 4, 4 },
        });

        var topLabel = new TextBlock { Text = $"+{max:0}", FontSize = 10, Foreground = textBrush };
        Canvas.SetLeft(topLabel, 2);
        Canvas.SetTop(topLabel, top - 2);
        canvas.Children.Add(topLabel);
        var botLabel = new TextBlock { Text = $"{min:0}", FontSize = 10, Foreground = textBrush };
        Canvas.SetLeft(botLabel, 2);
        Canvas.SetTop(botLabel, h - bottom - 12);
        canvas.Children.Add(botLabel);
        var zeroLabel = new TextBlock { Text = "0", FontSize = 10, Foreground = new SolidColorBrush(Palette.Own) };
        Canvas.SetLeft(zeroLabel, 2);
        Canvas.SetTop(zeroLabel, zeroY - 7);
        canvas.Children.Add(zeroLabel);

        var pts = new Points();
        foreach (var p in history)
        {
            if (p.T < tStart - 5) continue;
            pts.Add(new Point(X(p.T), Y(p.Gap)));
        }
        if (pts.Count < 2) return;

        canvas.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = new SolidColorBrush(Palette.Info),
            StrokeThickness = 2,
            StrokeJoin = PenLineJoin.Round,
        });

        var last = pts[pts.Count - 1];
        var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Palette.Info) };
        Canvas.SetLeft(dot, last.X - 4);
        Canvas.SetTop(dot, last.Y - 4);
        canvas.Children.Add(dot);
    }
}

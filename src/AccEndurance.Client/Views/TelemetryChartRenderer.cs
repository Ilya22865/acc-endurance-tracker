using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using AccEndurance.Client.Services;

namespace AccEndurance.Client.Views;

/// <summary>
/// Рисует один график телеметрии на Canvas: сетка, подписи, линия и заливка.
/// Элементы создаются один раз и только обновляются — график перерисовывается ~30 раз в секунду.
/// </summary>
public sealed class TelemetryChartRenderer
{
    private const double LeftPad = 46;
    private const double RightPad = 10;
    private const double TopPad = 8;
    private const double BottomPad = 18;
    private const int GridLines = 5; // горизонтальных линий (включая верх и низ)

    private readonly Canvas _canvas;
    private readonly Color _color;
    private readonly double _fixedMin;
    private readonly double _fixedMax;
    private readonly bool _autoScaleSymmetric;
    private readonly bool _percent;

    private readonly Line[] _hLines = new Line[GridLines];
    private readonly TextBlock[] _hLabels = new TextBlock[GridLines];
    private readonly Line[] _vLines = new Line[5];
    private readonly TextBlock[] _vLabels = new TextBlock[5];
    private readonly Polygon _fill = new();
    private readonly Polyline _line = new();
    private readonly Line _zeroLine = new();
    private readonly TextBlock _emptyText = new();

    /// <param name="percent">подписи оси в процентах (педали); иначе числа (руль)</param>
    /// <param name="autoScaleSymmetric">симметричная ось ±max с автоподбором (руль: единицы могут быть разные)</param>
    public TelemetryChartRenderer(Canvas canvas, Color color, double min, double max,
        bool percent, bool autoScaleSymmetric)
    {
        _canvas = canvas;
        _color = color;
        _fixedMin = min;
        _fixedMax = max;
        _percent = percent;
        _autoScaleSymmetric = autoScaleSymmetric;

        var grid = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x2F));
        var text = new SolidColorBrush(Color.FromRgb(0x70, 0x75, 0x7A));

        for (int i = 0; i < GridLines; i++)
        {
            _hLines[i] = new Line { Stroke = grid, StrokeThickness = 1 };
            _hLabels[i] = new TextBlock { FontSize = 10, Foreground = text };
            _canvas.Children.Add(_hLines[i]);
            _canvas.Children.Add(_hLabels[i]);
        }
        for (int i = 0; i < _vLines.Length; i++)
        {
            _vLines[i] = new Line { Stroke = grid, StrokeThickness = 1 };
            _vLabels[i] = new TextBlock { FontSize = 10, Foreground = text };
            _canvas.Children.Add(_vLines[i]);
            _canvas.Children.Add(_vLabels[i]);
        }

        _zeroLine.Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x60));
        _zeroLine.StrokeThickness = 1;
        _zeroLine.StrokeDashArray = new AvaloniaList<double> { 4, 4 };
        _zeroLine.IsVisible = autoScaleSymmetric;
        _canvas.Children.Add(_zeroLine);

        _fill.Fill = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B));
        _canvas.Children.Add(_fill);

        _line.Stroke = new SolidColorBrush(color);
        _line.StrokeThickness = 2;
        _line.StrokeJoin = PenLineJoin.Round;
        _canvas.Children.Add(_line);

        _emptyText.Text = "Нет данных — запустите агент (вкладка «Агент»)";
        _emptyText.FontSize = 12;
        _emptyText.Foreground = text;
        _canvas.Children.Add(_emptyText);
    }

    public void Render(IReadOnlyList<TelemetryPoint> data, double now, double windowSeconds,
        Func<TelemetryPoint, double> selector)
    {
        var w = _canvas.Bounds.Width;
        var h = _canvas.Bounds.Height;
        if (w <= LeftPad + RightPad + 20 || h <= TopPad + BottomPad + 20) return;

        var x0 = LeftPad;
        var x1 = w - RightPad;
        var y0 = TopPad;
        var y1 = h - BottomPad;
        var pw = x1 - x0;
        var ph = y1 - y0;
        var tStart = now - windowSeconds;

        // --- диапазон по Y ---
        double min = _fixedMin, max = _fixedMax;
        if (_autoScaleSymmetric)
        {
            var maxAbs = 1.0;
            for (int i = data.Count - 1; i >= 0 && data[i].T >= tStart; i--)
                maxAbs = Math.Max(maxAbs, Math.Abs(selector(data[i])));
            maxAbs = Math.Ceiling(maxAbs * 1.05 * 10) / 10;
            min = -maxAbs;
            max = maxAbs;
        }

        double Y(double v) => y1 - Math.Clamp((v - min) / (max - min), 0, 1) * ph;

        // --- сетка по Y ---
        for (int i = 0; i < GridLines; i++)
        {
            var frac = i / (double)(GridLines - 1);
            var y = y1 - frac * ph;
            var value = min + frac * (max - min);
            _hLines[i].StartPoint = new Point(x0, y);
            _hLines[i].EndPoint = new Point(x1, y);
            _hLabels[i].Text = _percent ? $"{value * 100:F0}%" : $"{value:0.0#}";
            Canvas.SetLeft(_hLabels[i], 4);
            Canvas.SetTop(_hLabels[i], y - 7);
        }

        // --- сетка по X (секунды назад) ---
        for (int i = 0; i < _vLines.Length; i++)
        {
            var frac = i / (double)(_vLines.Length - 1);
            var x = x0 + frac * pw;
            _vLines[i].StartPoint = new Point(x, y0);
            _vLines[i].EndPoint = new Point(x, y1);
            var secAgo = windowSeconds * (1 - frac);
            _vLabels[i].Text = secAgo < 0.5 ? "сейчас" : $"-{secAgo:F0}с";
            Canvas.SetLeft(_vLabels[i], Math.Min(x - 10, w - 40));
            Canvas.SetTop(_vLabels[i], y1 + 3);
        }

        if (_autoScaleSymmetric)
        {
            var yz = Y(0);
            _zeroLine.StartPoint = new Point(x0, yz);
            _zeroLine.EndPoint = new Point(x1, yz);
        }

        // --- кривая ---
        var first = data.Count;
        for (int i = 0; i < data.Count; i++)
        {
            if (data[i].T >= tStart) { first = Math.Max(0, i - 1); break; } // одна точка слева — линия входит в окно плавно
        }

        var count = data.Count - first;
        if (count < 2)
        {
            _line.IsVisible = false;
            _fill.IsVisible = false;
            _emptyText.IsVisible = true;
            Canvas.SetLeft(_emptyText, x0 + 12);
            Canvas.SetTop(_emptyText, y0 + ph / 2 - 8);
            return;
        }
        _emptyText.IsVisible = false;
        _line.IsVisible = true;
        _fill.IsVisible = true;

        var stride = Math.Max(1, count / 700); // не больше ~700 точек на график
        var pts = new Points();
        double lastX = x0, firstX = x0;
        for (int i = first; i < data.Count; i += stride)
        {
            var p = data[i];
            var x = x0 + Math.Clamp((p.T - tStart) / windowSeconds, 0, 1) * pw;
            if (pts.Count == 0) firstX = x;
            lastX = x;
            pts.Add(new Point(x, Y(selector(p))));
        }
        // обязательно добавляем последнюю точку
        var lastPoint = data[data.Count - 1];
        if ((data.Count - 1 - first) % stride != 0)
        {
            lastX = x0 + Math.Clamp((lastPoint.T - tStart) / windowSeconds, 0, 1) * pw;
            pts.Add(new Point(lastX, Y(selector(lastPoint))));
        }

        _line.Points = pts;

        var baseY = _autoScaleSymmetric ? Y(0) : y1;
        var poly = new Points();
        foreach (var p in pts) poly.Add(p);
        poly.Add(new Point(lastX, baseY));
        poly.Add(new Point(firstX, baseY));
        _fill.Points = poly;
    }
}

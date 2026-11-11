using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using EnduranceHub.Client.Services;

namespace EnduranceHub.Client.Views;

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
    private readonly bool _showGrid;

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
        bool percent, bool autoScaleSymmetric, bool showGrid = true)
    {
        _showGrid = showGrid;
        _canvas = canvas;
        _color = color;
        _fixedMin = min;
        _fixedMax = max;
        _percent = percent;
        _autoScaleSymmetric = autoScaleSymmetric;

        var grid = new SolidColorBrush(Palette.Line);
        var text = new SolidColorBrush(Palette.Text3);

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

        _zeroLine.Stroke = new SolidColorBrush(Palette.LineStrong);
        _zeroLine.StrokeThickness = 1;
        _zeroLine.StrokeDashArray = new AvaloniaList<double> { 4, 4 };
        _zeroLine.IsVisible = autoScaleSymmetric && showGrid;
        _canvas.Children.Add(_zeroLine);

        _fill.Fill = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B));
        _canvas.Children.Add(_fill);

        _line.Stroke = new SolidColorBrush(color);
        _line.StrokeThickness = 2;
        _line.StrokeJoin = PenLineJoin.Round;
        _canvas.Children.Add(_line);

        _emptyText.Text = "Нет данных — запустите агент (вкладка «Агент»)";
        _emptyText.FontSize = 13;
        _emptyText.IsVisible = showGrid;
        _emptyText.Foreground = text;
        _canvas.Children.Add(_emptyText);

        if (!showGrid)
        {
            foreach (var l in _hLines) l.IsVisible = false;
            foreach (var l in _vLines) l.IsVisible = false;
            foreach (var t in _hLabels) t.IsVisible = false;
            foreach (var t in _vLabels) t.IsVisible = false;
        }
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
        for (int i = 0; _showGrid && i < GridLines; i++)
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
        for (int i = 0; _showGrid && i < _vLines.Length; i++)
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

        if (_autoScaleSymmetric && _showGrid)
        {
            var yz = Y(0);
            _zeroLine.StartPoint = new Point(x0, yz);
            _zeroLine.EndPoint = new Point(x1, yz);
        }

        // --- кривая ---
        // Рисуем только «наступившие» точки (T <= now), а не все подряд: раньше
        // точки из ещё не пришедшей пачки прижимались к правому краю и давали
        // вертикальную «пилу». Края окна интерполируем — линия движется непрерывно.
        var startIdx = LowerBound(data, tStart);
        var endIdx = LowerBound(data, now); // первая точка с T >= now
        var lastVisible = endIdx < data.Count && data[endIdx].T <= now ? endIdx : endIdx - 1;

        if (startIdx >= data.Count || lastVisible < startIdx)
        {
            _line.IsVisible = false;
            _fill.IsVisible = false;
            _emptyText.IsVisible = _showGrid;
            Canvas.SetLeft(_emptyText, x0 + 12);
            Canvas.SetTop(_emptyText, y0 + ph / 2 - 8);
            return;
        }

        var pts = new Points();

        // левый край окна: интерполяция между точкой до окна и первой точкой в окне
        if (startIdx > 0 && data[startIdx].T > tStart)
        {
            var a = data[startIdx - 1];
            var b = data[startIdx];
            var span = b.T - a.T;
            var f = span > 1e-9 ? Math.Clamp((tStart - a.T) / span, 0, 1) : 1;
            pts.Add(new Point(x0, Y(Lerp(selector(a), selector(b), f))));
        }

        // точки внутри окна (не больше ~700 на график)
        var visibleCount = lastVisible - startIdx + 1;
        var stride = Math.Max(1, visibleCount / 700);
        for (int i = startIdx; i <= lastVisible; i += stride)
        {
            var p = data[i];
            var x = x0 + Math.Clamp((p.T - tStart) / windowSeconds, 0, 1) * pw;
            pts.Add(new Point(x, Y(selector(p))));
        }

        // правый край: интерполируем значение ровно на момент now
        if (lastVisible + 1 < data.Count)
        {
            var a = data[lastVisible];
            var b = data[lastVisible + 1];
            var span = b.T - a.T;
            var f = span > 1e-9 ? Math.Clamp((now - a.T) / span, 0, 1) : 1;
            pts.Add(new Point(x1, Y(Lerp(selector(a), selector(b), f))));
        }
        else if (now - data[lastVisible].T < 1.0)
        {
            // новых данных нет, но последняя точка свежая — дотягиваем линию до края
            pts.Add(new Point(x1, Y(selector(data[lastVisible]))));
        }

        if (pts.Count < 2)
        {
            _line.IsVisible = false;
            _fill.IsVisible = false;
            _emptyText.IsVisible = _showGrid;
            Canvas.SetLeft(_emptyText, x0 + 12);
            Canvas.SetTop(_emptyText, y0 + ph / 2 - 8);
            return;
        }

        _emptyText.IsVisible = false;
        _line.IsVisible = true;
        _fill.IsVisible = true;
        _line.Points = pts;

        var baseY = _autoScaleSymmetric ? Y(0) : y1;
        var poly = new Points();
        foreach (var p in pts) poly.Add(p);
        poly.Add(new Point(pts[pts.Count - 1].X, baseY));
        poly.Add(new Point(pts[0].X, baseY));
        _fill.Points = poly;
    }

    /// <summary>Первый индекс, где T >= value (данные отсортированы по времени).</summary>
    private static int LowerBound(IReadOnlyList<TelemetryPoint> data, double value)
    {
        int lo = 0, hi = data.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (data[mid].T < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static double Lerp(double from, double to, double f) => from + (to - from) * f;
}

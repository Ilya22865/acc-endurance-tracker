using Avalonia.Media;

namespace EnduranceHub.Client;

/// <summary>Цвета для кода (графики, радар, карта). Должны совпадать со Styles/Theme.axaml.</summary>
public static class Palette
{
    public static readonly Color Bg = Color.Parse("#151922");
    public static readonly Color Surface = Color.Parse("#1C222E");
    public static readonly Color Surface2 = Color.Parse("#252D3B");
    public static readonly Color Surface3 = Color.Parse("#2F394B");
    public static readonly Color Line = Color.Parse("#2C3444");
    public static readonly Color LineStrong = Color.Parse("#3D485C");
    public static readonly Color Text = Color.Parse("#EAEEF5");
    public static readonly Color Text2 = Color.Parse("#A6B0C3");
    public static readonly Color Text3 = Color.Parse("#748096");

    public static readonly Color Own = Color.Parse("#FF6B4A");
    public static readonly Color Best = Color.Parse("#C084FC");
    public static readonly Color Good = Color.Parse("#34D399");
    public static readonly Color Warn = Color.Parse("#FFC247");
    public static readonly Color Danger = Color.Parse("#FF4D6D");
    public static readonly Color Info = Color.Parse("#5AA9FF");

    public static IBrush Brush(Color c) => new SolidColorBrush(c);
    public static IBrush Brush(Color c, byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
}

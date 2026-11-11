using Avalonia.Media;

namespace EnduranceHub.Client;

/// <summary>Цвета для кода (графики, радар, карта). Должны совпадать со Styles/Theme.axaml.</summary>
public static class Palette
{
    public static readonly Color Bg = Color.Parse("#0B0D10");
    public static readonly Color Surface = Color.Parse("#111419");
    public static readonly Color Surface2 = Color.Parse("#171B21");
    public static readonly Color Surface3 = Color.Parse("#21262E");
    public static readonly Color Line = Color.Parse("#232830");
    public static readonly Color LineStrong = Color.Parse("#343B45");
    public static readonly Color Text = Color.Parse("#F0F2F5");
    public static readonly Color Text2 = Color.Parse("#A4ABB5");
    public static readonly Color Text3 = Color.Parse("#6F7782");

    /// <summary>Сигнальный акцент: наша машина, активная навигация, главные кнопки.</summary>
    public static readonly Color Accent = Color.Parse("#FF5A1F");
    /// <summary>«Своя» машина: белая табличка с тёмным номером.</summary>
    public static readonly Color Own = Color.Parse("#F2F4F7");
    /// <summary>Тёмный текст на белой/акцентной плашке.</summary>
    public static readonly Color OnOwn = Color.Parse("#0B0D10");

    public static readonly Color Best = Color.Parse("#A78BFA");
    public static readonly Color Good = Color.Parse("#5BB98C");
    public static readonly Color Warn = Color.Parse("#D9A441");
    public static readonly Color Danger = Color.Parse("#F2555A");
    public static readonly Color Info = Color.Parse("#6B9FD4");

    public static IBrush Brush(Color c) => new SolidColorBrush(c);
    public static IBrush Brush(Color c, byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
}

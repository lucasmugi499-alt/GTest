using Cascade.Sim;
using Godot;

/// <summary>Placeholder colours (M5 is functional, not pretty).</summary>
public static class Palette
{
    public static readonly Color Background = new(0.09f, 0.1f, 0.12f);
    public static readonly Color Panel = new(0.14f, 0.15f, 0.18f);
    public static readonly Color Text = new(0.9f, 0.9f, 0.88f);
    public static readonly Color Muted = new(0.6f, 0.62f, 0.66f);
    public static readonly Color Ok = new(0.27f, 0.68f, 0.39f);
    public static readonly Color Strained = new(0.93f, 0.66f, 0.2f);
    public static readonly Color Failing = new(0.86f, 0.26f, 0.24f);
    public static readonly Color Inactive = new(0.4f, 0.42f, 0.45f);
    public static readonly Color Player = new(0.22f, 0.36f, 0.52f);
    public static readonly Color Rival = new(0.45f, 0.3f, 0.24f);

    public static Color Of(Health h) => h switch
    {
        Health.Ok => Ok,
        Health.Strained => Strained,
        Health.Failing => Failing,
        _ => Inactive,
    };

    public static StyleBoxFlat Box(Color c, int margin = 8)
    {
        var s = new StyleBoxFlat { BgColor = c };
        s.SetContentMarginAll(margin);
        s.SetCornerRadiusAll(4);
        return s;
    }

    public static Label Label(string text, int size = 14, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Text);
        return l;
    }
}

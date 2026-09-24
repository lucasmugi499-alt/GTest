using Cascade.Sim;
using Godot;

/// <summary>The storylet dialog: the card, its choices (greyed out if unavailable), and each choice's hint.</summary>
public partial class StoryletDialog : Control
{
    private SimController _c = null!;
    private VBoxContainer _body = null!;
    public event Action? Closed;

    public void Build(SimController c)
    {
        _c = c;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.6f), AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
        panel.AddThemeStyleboxOverride("panel", Palette.Box(Palette.Panel, 20));
        center.AddChild(panel);
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(_body);
        Visible = false;
    }

    public void Open(DecisionView d)
    {
        foreach (var child in _body.GetChildren()) child.QueueFree();
        _body.AddChild(Palette.Label($"{(d.Major ? "DECISION" : "BRIEF")}  ·  {_c.Sim.Calendar.Describe(d.Day)}{(d.Hour >= 0 ? $" {d.Hour:00}:00" : "")}", 12, Palette.Muted));
        _body.AddChild(Palette.Label(d.Title, 22));
        var text = Palette.Label(d.Text, 15);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.CustomMinimumSize = new Vector2(720, 0);
        _body.AddChild(text);
        foreach (var ch in d.Choices)
        {
            var b = new Button { Text = $"{ch.Index + 1}.  {ch.Text}", Disabled = !ch.Available, Alignment = HorizontalAlignment.Left };
            b.AddThemeFontSizeOverride("font_size", 15);
            int index = ch.Index;
            b.Pressed += () => { _c.Choose(d, index); Close(); };
            if (!ch.Available) b.TooltipText = "Not available now (not enough Political Capital, or the conditions aren't met).";
            _body.AddChild(b);
            if (ch.Hint.Length > 0)
            {
                var hint = Palette.Label($"      {ch.Hint}", 12, Palette.Muted);
                hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                _body.AddChild(hint);
            }
        }
        if (!d.Major)
        {
            if (d.ExpiresDay >= 0)
                _body.AddChild(Palette.Label($"If you don't answer by the end of {_c.Sim.Calendar.Describe(d.ExpiresDay)}: \"{d.DefaultChoice}\"", 12, Palette.Muted));
            var later = new Button { Text = "Decide later" };
            later.Pressed += Close;
            _body.AddChild(later);
        }
        Visible = true;
    }

    private void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }
}

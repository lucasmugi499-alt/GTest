using Cascade.Sim;
using Godot;

/// <summary>Brief cards: every decision waiting for you, majors first. Click one to open it.</summary>
public partial class BriefPanel : VBoxContainer
{
    private SimController _c = null!;
    private StoryletDialog _dialog = null!;
    private string _shown = "";

    public void Bind(SimController c, StoryletDialog dialog)
    {
        _c = c;
        _dialog = dialog;
        c.Changed += Refresh;
    }

    private void Refresh()
    {
        var cards = _c.Snapshot.Decisions.Where(d => !_c.IsAnswered(d.Seq)).OrderBy(d => d.Major ? 0 : 1).ThenBy(d => d.Seq).ToList();
        var key = string.Join(",", cards.Select(d => d.Seq));
        if (key == _shown) return;
        _shown = key;
        foreach (var child in GetChildren()) child.QueueFree();
        AddChild(Palette.Label($"BRIEF  ({cards.Count})", 13, Palette.Muted));
        if (cards.Count == 0) AddChild(Palette.Label("Nothing waiting for you.", 13, Palette.Muted));
        foreach (var d in cards)
        {
            var b = new Button
            {
                Text = $"{(d.Major ? "■ " : "□ ")}{d.Title}{(d.ExpiresDay >= 0 ? $"   (until {_c.Sim.Calendar.Describe(d.ExpiresDay).Split(" · ")[0]})" : "")}",
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
            };
            b.AddThemeColorOverride("font_color", d.Major ? Palette.Strained : Palette.Text);
            var card = d;
            b.Pressed += () => _dialog.Open(card);
            AddChild(b);
        }
    }
}

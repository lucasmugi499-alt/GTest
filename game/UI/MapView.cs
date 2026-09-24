using Cascade.Sim;
using Godot;

/// <summary>
/// The province map (placeholder): each province is a rectangle from content, shaded by how much of it is dark and
/// outlined red in Crisis Time. The Veyl front sits on the border; the escalation gauge is top right.
/// </summary>
public partial class MapView : Control
{
    private SimController _c = null!;

    public void Bind(SimController c)
    {
        _c = c;
        c.Changed += QueueRedraw;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
    }

    private Rect2 Rect(ProvinceView p)
    {
        var s = Size;
        return new Rect2(p.MapX / 100f * s.X, p.MapY / 100f * s.Y, p.MapW / 100f * s.X, p.MapH / 100f * s.Y);
    }

    public override void _Draw()
    {
        if (_c?.Snapshot is null) return;
        var snap = _c.Snapshot;
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Background);

        foreach (var p in snap.Provinces)
        {
            var r = Rect(p);
            bool player = p.Owner == "kestria";
            var fill = player ? Palette.Player : Palette.Rival;
            if (player && p.Population > 0)
            {
                // Darker as more people lose power.
                float dark = (float)p.PeopleWithoutPower / p.Population;
                fill = fill.Lerp(new Color(0.03f, 0.03f, 0.05f), dark * 0.85f);
            }
            DrawRect(r, fill);
            DrawRect(r, p.InCrisis ? Palette.Failing : Palette.Muted, false, p.InCrisis ? 4 : 1.5f);

            var at = r.Position + new Vector2(8, 22);
            DrawString(font, at, p.Name, HorizontalAlignment.Left, r.Size.X - 12, 16, Palette.Text);
            if (!player) continue;
            int line = 1;
            void Line(string text, Color color) => DrawString(font, at + new Vector2(0, 20 * line++), text, HorizontalAlignment.Left, r.Size.X - 12, 13, color);
            Line($"{p.Population / 1e6:0.0}M people", Palette.Muted);
            if (p.PeopleWithoutPower > 0) Line($"{p.PeopleWithoutPower / 1e6:0.0}M in the dark", Palette.Failing);
            if (p.SubstationsDown > 0) Line($"{p.SubstationsDown}/{p.Substations} substations down", Palette.Strained);
            if (p.GridCollapsed) Line("GRID COLLAPSED", Palette.Failing);
            if (p.InternetShutdown) Line("internet shut down", Palette.Strained);
            if (p.InCrisis) Line("CRISIS TIME", Palette.Failing);
        }

        // The Veyl front: on the border between Veyl and Varan.
        var veyl = snap.Provinces.First(p => p.Id == "veyl");
        var vr = Rect(veyl);
        var x = vr.End.X;
        var front = snap.Front;
        var col = front.Active ? Palette.Failing : front.Locked ? Palette.Strained : Palette.Muted;
        for (float y = vr.Position.Y; y < vr.End.Y; y += 12) DrawLine(new Vector2(x, y), new Vector2(x, y + 7), col, 4);
        DrawString(font, new Vector2(x + 6, vr.Position.Y - 6), front.Active ? "FRONT: FIGHTING" : "front: quiet", HorizontalAlignment.Left, -1, 12, col);
        int row = 0;
        foreach (var b in front.Brigades.Where(b => b.Strength > 0))
            DrawString(font, new Vector2(x + 6, vr.End.Y + 16 + 16 * row++), $"{b.Name}: {b.Strength:N0}", HorizontalAlignment.Left, -1, 12, Palette.Text);

        // Escalation gauge.
        var p0 = snap.Politics;
        var g = new Rect2(16, Size.Y - 44, 260, 16);
        DrawRect(g, Palette.Panel);
        DrawRect(new Rect2(g.Position, new Vector2(g.Size.X * (float)(p0.EscalationMeter / 100), g.Size.Y)), p0.Rung >= 5 ? Palette.Failing : p0.Rung >= 3 ? Palette.Strained : Palette.Ok);
        DrawString(font, g.Position + new Vector2(0, -6), $"Escalation {p0.EscalationMeter:0} · rung {p0.Rung}", HorizontalAlignment.Left, -1, 13, Palette.Text);
        // The estimated red line as a tick on the gauge.
        float est = g.Position.X + g.Size.X * (float)Math.Clamp(p0.RivalRedLineEstimate / 100, 0, 1);
        DrawLine(new Vector2(est, g.Position.Y - 3), new Vector2(est, g.End.Y + 3), Palette.Failing, 2);
        DrawString(font, new Vector2(g.End.X + 10, g.End.Y - 2), $"estimated red line ~{p0.RivalRedLineEstimate:0}", HorizontalAlignment.Left, -1, 12, Palette.Muted);
    }

    public override string _GetTooltip(Vector2 at)
    {
        if (_c?.Snapshot is null) return "";
        foreach (var p in _c.Snapshot.Provinces)
            if (Rect(p).HasPoint(at))
                return p.Owner == "kestria"
                    ? $"{p.Name}: power served {p.PowerServed:P0}, {p.PeopleWithoutPower:N0} people without power, {p.SubstationsDown}/{p.Substations} substations down."
                    : $"{p.Name} ({p.Owner}).";
        return "";
    }
}

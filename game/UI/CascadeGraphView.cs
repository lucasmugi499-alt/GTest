using Cascade.Sim;
using Godot;

/// <summary>
/// The Cascade view (placeholder): facilities, goods, power and imports as a node graph, left to right from sources to
/// the front, coloured green / amber / red / grey by health. Hover a node for why.
/// </summary>
public partial class CascadeGraphView : Control
{
    private SimController _c = null!;
    private CascadeView? _view;
    private readonly Dictionary<string, Rect2> _boxes = [];

    public void Bind(SimController c)
    {
        _c = c;
        c.Changed += () => { if (IsVisibleInTree()) Refresh(); };
        VisibilityChanged += () => { if (IsVisibleInTree()) Refresh(); };
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
    }

    private void Refresh()
    {
        if (_c?.Sim is null) return;
        _view = CascadeGraph.Build(_c.Sim);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Background);
        if (_view is null) return;
        var font = ThemeDB.FallbackFont;
        float colW = Size.X / _view.Columns;
        float rowH = Math.Min(64, (Size.Y - 40) / Math.Max(1, _view.Rows));
        float boxW = colW - 18, boxH = rowH - 10;
        _boxes.Clear();
        foreach (var n in _view.Nodes)
            _boxes[n.Id] = new Rect2(n.Column * colW + 9, 30 + n.Row * rowH, boxW, boxH);

        foreach (var e in _view.Edges)
        {
            if (!_boxes.TryGetValue(e.From, out var a) || !_boxes.TryGetValue(e.To, out var b)) continue;
            var from = new Vector2(a.End.X, a.Position.Y + a.Size.Y / 2);
            var to = new Vector2(b.Position.X, b.Position.Y + b.Size.Y / 2);
            var mid = (from.X + to.X) / 2;
            var pts = new Vector2[17];
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f, u = 1 - t;
                // Cubic Bézier with horizontal tangents.
                pts[i] = u * u * u * from + 3 * u * u * t * new Vector2(mid, from.Y) + 3 * u * t * t * new Vector2(mid, to.Y) + t * t * t * to;
            }
            var col = Palette.Of(e.Health);
            col.A = e.Kind == "power" ? 0.9f : 0.55f;
            DrawPolyline(pts, col, e.Kind == "power" ? 2.5f : 1.5f, true);
        }

        foreach (var n in _view.Nodes)
        {
            var r = _boxes[n.Id];
            DrawRect(r, Palette.Panel);
            DrawRect(new Rect2(r.Position, new Vector2(6, r.Size.Y)), Palette.Of(n.Health));
            DrawRect(r, Palette.Of(n.Health), false, 1.5f);
            DrawMultilineString(font, r.Position + new Vector2(10, 15), n.Label, HorizontalAlignment.Left, r.Size.X - 14, 11, 2, Palette.Text);
            DrawString(font, r.Position + new Vector2(10, r.Size.Y - 5), n.Kind, HorizontalAlignment.Left, r.Size.X - 14, 9, Palette.Muted);
        }

        DrawString(font, new Vector2(10, 18), "Power and imports  →  refined goods  →  fabs  →  components  →  drone line  →  front.   Hover a box for why.",
            HorizontalAlignment.Left, -1, 12, Palette.Muted);
    }

    public override string _GetTooltip(Vector2 at)
    {
        if (_view is null) return "";
        foreach (var n in _view.Nodes)
            if (_boxes.TryGetValue(n.Id, out var r) && r.HasPoint(at)) return $"{n.Label}\n{n.Detail}";
        return "";
    }
}

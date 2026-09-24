using System.Globalization;
using System.Text;
using Cascade.Sim;
using Cascade.Sim.Core;
using Cascade.Sim.Narrative;
using Godot;

/// <summary>
/// CASCADE · The Veyl Crossing, minimal UI (M5, functional placeholders): top bar with the date, pause and speeds 1–3,
/// and the readouts; tabs for the province map, the Cascade view, society and the Chronicle; the Brief, orders and the
/// log on the right; and the storylet dialog on top.
///
/// Command line (after "--"): --seed N, --auto first|default|random (plays itself to the end and quits),
/// --screenshot FILE --screenshot-day D [--tab N] (saves a picture of the screen on day D and quits).
/// </summary>
public partial class Main : Control
{
    private SimController _c = null!;
    private Label _when = null!, _readouts = null!, _crisis = null!;
    private readonly List<Button> _speedButtons = [];
    private RichTextLabel _log = null!, _society = null!, _chronicle = null!;
    private TabContainer _tabs = null!;
    private StoryletDialog _dialog = null!;
    private int _logShown;

    private string? _screenshot;
    private int _screenshotDay = -1;
    private int _screenshotFrames;
    private bool _frozen;

    public override void _Ready()
    {
        var args = ParseArgs(OS.GetCmdlineUserArgs());
        _c = new SimController();
        AddChild(_c);
        BuildUi();
        _c.Changed += Refresh;
        _c.Finished += OnFinished;
        _c.DecisionNeeded += _dialog.Open;

        _c.Start(args.TryGetValue("seed", out var seed) ? ulong.Parse(seed, CultureInfo.InvariantCulture) : null);
        if (args.TryGetValue("auto", out var auto)) _c.Autopilot = Enum.Parse<AutoMode>(auto, ignoreCase: true);
        if (args.TryGetValue("screenshot", out var shot))
        {
            _screenshot = shot;
            _screenshotDay = args.TryGetValue("screenshot-day", out var d) ? int.Parse(d, CultureInfo.InvariantCulture) : 0;
            _c.Autopilot ??= AutoMode.Default;
            if (args.TryGetValue("tab", out var tab)) _tabs.CurrentTab = int.Parse(tab, CultureInfo.InvariantCulture);
        }
        GD.Print($"[cascade] {_c.Sim.Scenario.Name} started, seed {_c.Sim.Seed}");
    }

    private static Dictionary<string, string> ParseArgs(string[] a)
    {
        var o = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
            if (a[i].StartsWith("--")) o[a[i][2..]] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
        return o;
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = Palette.Background, AnchorRight = 1, AnchorBottom = 1 });
        var root = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1 };
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        // Top bar: date, crisis flag, speed controls, readouts.
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", Palette.Box(Palette.Panel, 8));
        root.AddChild(bar);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        bar.AddChild(top);
        _when = Palette.Label("", 16);
        _when.CustomMinimumSize = new Vector2(250, 0);
        top.AddChild(_when);
        _crisis = Palette.Label("", 14, Palette.Failing);
        _crisis.CustomMinimumSize = new Vector2(110, 0);
        top.AddChild(_crisis);
        foreach (var (label, speed) in new[] { ("⏸", 0), ("▶ 1", 1), ("▶▶ 2", 2), ("▶▶▶ 3", 3) })
        {
            var b = new Button { Text = label, ToggleMode = true, TooltipText = speed == 0 ? "Pause" : $"Speed {speed}: {new[] { 0, 4, 2, 1 }[speed]} s per day" };
            b.Pressed += () => _c.SetSpeed(speed);
            _speedButtons.Add(b);
            top.AddChild(b);
        }
        var step = new Button { Text = "Step", TooltipText = "Advance one step (an hour in Crisis Time, else a day)." };
        step.Pressed += () => _c.StepOnce();
        top.AddChild(step);
        _readouts = Palette.Label("", 14);
        _readouts.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(_readouts);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(split);

        _tabs = new TabContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(_tabs);
        var map = new MapView { Name = "Map" };
        map.Bind(_c);
        _tabs.AddChild(map);
        var cascade = new CascadeGraphView { Name = "Cascade" };
        cascade.Bind(_c);
        _tabs.AddChild(cascade);
        _society = Rich("Society");
        _tabs.AddChild(_society);
        _chronicle = Rich("Chronicle");
        _tabs.AddChild(_chronicle);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        right.AddThemeConstantOverride("separation", 10);
        split.AddChild(right);
        _dialog = new StoryletDialog();
        var brief = new BriefPanel();
        brief.Bind(_c, _dialog);
        right.AddChild(Framed(brief));
        var orders = new OrdersPanel();
        orders.Bind(_c);
        right.AddChild(Framed(orders));
        _log = Rich("Log");
        _log.SizeFlagsVertical = SizeFlags.ExpandFill;
        _log.ScrollFollowing = true;
        right.AddChild(_log);

        _dialog.Build(_c);
        _dialog.Closed += () => { if (_c.AutoPaused && !_c.Snapshot.Decisions.Any(d => d.Major && !_c.IsAnswered(d.Seq))) _c.SetSpeed(1); };
        AddChild(_dialog);
    }

    private static RichTextLabel Rich(string name)
    {
        var r = new RichTextLabel { Name = name, BbcodeEnabled = true, SelectionEnabled = true };
        r.AddThemeFontSizeOverride("normal_font_size", 13);
        r.AddThemeFontSizeOverride("bold_font_size", 14);
        r.AddThemeStyleboxOverride("normal", Palette.Box(Palette.Background, 10));
        return r;
    }

    private static PanelContainer Framed(Control inner)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Palette.Box(Palette.Panel, 10));
        p.AddChild(inner);
        return p;
    }

    private void Refresh()
    {
        var s = _c.Snapshot;
        var p = s.Politics;
        _when.Text = _c.When();
        _crisis.Text = _c.Sim.IsCrisisDay || s.Provinces.Any(x => x.InCrisis && x.Owner == "kestria") ? "CRISIS TIME" : "";
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].ButtonPressed = i == _c.Speed;
        long dark = s.Provinces.Where(x => x.Owner == "kestria").Sum(x => x.PeopleWithoutPower);
        _readouts.Text =
            $"Magnets {Cover(s.Good("rare_earth_magnet"))}   Controllers {Cover(s.Good("flight_controller"))}   " +
            $"Chips {s.Good("legacy_chip").ProducedToday / 1e6:0.00}M/d   Drones {s.Good("fpv_strike_drone").ProducedToday:0}/d   " +
            $"Dark {dark / 1e6:0.0}M   Approval {p.Approval:0}   Capital {p.PoliticalCapital:0}   Trust {p.Trust:0}   Rung {p.Rung}";

        for (; _logShown < s.Log.Count; _logShown++)
        {
            var e = s.Log[_logShown];
            if (e.Kind is "ai_intent" or "storylet") continue;
            var color = e.Kind switch { "headline" => "#f0b040", "escalation" or "grid" => "#e06050", "decision" => "#80b0e0", _ => "#c8c8c8" };
            _log.AppendText($"[color=#808890]{_c.Sim.Calendar.Describe(e.Day).Split(" · ")[0]}{(e.Hour >= 0 ? $" {e.Hour:00}:00" : "")}[/color] [color={color}]{Escape(e.Text)}[/color]\n");
        }
        if (_society.IsVisibleInTree()) _society.Text = SocietyReport(s);

        if (_screenshot is not null && !_frozen && _c.Sim.Day > _screenshotDay)
        {
            _frozen = true;
            _c.Autopilot = null;
            _c.SetSpeed(0);
            var waiting = _c.Snapshot.Decisions.OrderBy(d => d.Major ? 0 : 1).FirstOrDefault(d => !_c.IsAnswered(d.Seq));
            if (waiting is not null) _dialog.Open(waiting);
        }
    }

    private static string Cover(GoodView g) => g.DaysOfCover is null ? "–" : $"{g.DaysOfCover:0}d{(g.Shortage == 2 ? "!!" : g.Shortage == 1 ? "!" : "")}";
    private static string Escape(string t) => t.Replace("[", "[lb]");

    private static string SocietyReport(SimSnapshot s)
    {
        var p = s.Politics;
        var b = new StringBuilder();
        b.AppendLine($"[b]The country[/b]  approval {p.Approval:0.0} · trust {p.Trust:0.0} · war support {p.WarSupport:0.0} · rally {p.Rally:0} · exhaustion {p.WarExhaustion:0.0} · inflation {p.Inflation:0.0}%");
        b.AppendLine($"Political Capital {p.PoliticalCapital:0} · emergency {(p.Emergency ? "IN FORCE" : "no")} · backsliding {p.Backsliding:0} · mobilization {p.Mobilization} · manpower pool {p.ManpowerPool:N0}");
        b.AppendLine($"Escalation {p.EscalationMeter:0.0} (rung {p.Rung}) · war-risk ×{p.InsuranceMultiplier:0} · shipping lines calling {p.ShippingLinesCalling}/{p.ShippingLines} · killed in action {p.KilledInAction:0}");
        if (p.Precedents.Count > 0) b.AppendLine($"Precedents: {string.Join(", ", p.Precedents)}");
        b.AppendLine();
        b.AppendLine("[b]Segments[/b]   (needs: power · prices · jobs · safety · connectivity · services · dignity)");
        foreach (var seg in s.Segments)
            b.AppendLine($"{seg.Name}: satisfaction {seg.Satisfaction:0} · align {seg.Align:0} · trust {seg.Trust:0} · needs {string.Join(" ", seg.Needs.Select(n => $"{n:0}"))}");
        b.AppendLine();
        b.AppendLine("[b]Factions[/b]");
        foreach (var f in s.Factions) b.AppendLine($"{f.Name}: approval {f.Approval:0} · leverage {f.Leverage:0}");
        b.AppendLine();
        b.AppendLine("[b]Narratives[/b]");
        foreach (var n in s.Narratives)
            b.AppendLine($"{Escape(n.Name)}: believing up to {n.Believing.Max():P0}, established in {n.EstablishedSegments} segment(s){(n.RumorHours > 0 ? $", next tipping in ~{n.RumorHours:0} h" : "")}{(n.TakenDown ? ", taken down" : "")}");
        b.AppendLine();
        b.AppendLine("[b]Cyber[/b]");
        foreach (var o in s.Operations) b.AppendLine($"{o.Id} ({o.Attacker}): {o.State}, access {o.Access:0}{(o.Attribution > 0 ? $", attribution {o.Attribution:0.00}" : "")}");
        return b.ToString();
    }

    private void OnFinished()
    {
        var c = ChronicleView.Build(_c.Sim);
        var b = new StringBuilder();
        b.AppendLine($"[b]THE CHRONICLE[/b]   {Escape(c.Title)}\n");
        b.AppendLine("[b]Headlines[/b]");
        foreach (var h in c.Headlines) b.AppendLine($"  {h.Date}   [color=#f0b040]{Escape(h.Text)}[/color]");
        b.AppendLine("\n[b]What happened[/b]");
        foreach (var e in c.Timeline) b.AppendLine($"  [color=#808890]{e.Date}{(e.Hour >= 0 ? $" {e.Hour:00}:00" : "")}[/color]  {Escape(e.Text)}");
        b.AppendLine("\n[b]History's verdict[/b]");
        b.AppendLine("  " + string.Join("   ", c.Scores.Select(kv => $"{kv.Key} {kv.Value:0}")));
        foreach (var v in c.Verdicts) b.AppendLine($"\n  [b]{v.Name}[/b], {v.School} ({v.Score:0}/100)\n  [i]\"{Escape(v.Verdict)}\"[/i]");
        b.AppendLine("\n[b]DECLASSIFIED[/b]");
        foreach (var d in c.Declassified) b.AppendLine($"  • {Escape(d)}");
        _chronicle.Text = b.ToString();
        _tabs.CurrentTab = 3;
        GD.Print($"[cascade] Day 90 complete. Final state hash {StateHasher.Format(_c.Sim.StateHash())}. Scores: {string.Join(", ", c.Scores.Select(kv => $"{kv.Key} {kv.Value:0}"))}");
        if (_c.Autopilot is not null && _screenshot is null) GetTree().Quit();
    }

    public override void _Process(double delta)
    {
        if (_screenshot is null || _c.Sim.Day <= _screenshotDay) return;
        // Let the frame draw, then save it.
        if (++_screenshotFrames < 5) return;
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(_screenshot);
        GD.Print($"[cascade] screenshot saved: {_screenshot}");
        GetTree().Quit();
    }
}

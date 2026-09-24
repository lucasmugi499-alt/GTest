using Cascade.Sim.Conflict;
using Cascade.Sim.Economy;
using Cascade.Sim.World;
using Godot;

/// <summary>The main levers outside storylets (placeholder): repairs, mobilization, emergency, sweep, patch, attack.</summary>
public partial class OrdersPanel : VBoxContainer
{
    private SimController _c = null!;
    private VBoxContainer _repairs = null!;
    private Label _feedback = null!;

    public void Bind(SimController c)
    {
        _c = c;
        AddChild(Palette.Label("ORDERS", 13, Palette.Muted));
        int me() => _c.Sim.World.Nations.Player;

        var mob = new HBoxContainer();
        mob.AddChild(Palette.Label("Mobilize:", 13));
        for (int level = 0; level <= 2; level++)
        {
            int l = level;
            var b = new Button { Text = $"{l}", TooltipText = l switch { 0 => "Peacetime", 1 => "Heightened readiness (7 days)", _ => "Partial mobilization, 40,000 reservists (14 days)" } };
            b.Pressed += () => Send(new SetMobilizationOrder(me(), l), $"Mobilization level {l} ordered.");
            mob.AddChild(b);
        }
        AddChild(mob);

        var row = new HBoxContainer();
        Add(row, "Declare emergency", () => new DeclareEmergencyOrder(me()));
        Add(row, "End emergency", () => new EndEmergencyOrder(me()));
        AddChild(row);
        var row2 = new HBoxContainer();
        Add(row2, "Sweep Ossen grid", () => new ForensicSweepOrder(me(), _c.Sim.World.Provinces.IdOf("kestria_east_ossen")));
        Add(row2, "Firmware patch", () => new FirmwarePatchOrder(me(), 0));
        Add(row2, "Attack at Veyl", () => new FrontAttackOrder(me()));
        AddChild(row2);

        AddChild(Palette.Label("Damaged substations:", 13, Palette.Muted));
        _repairs = new VBoxContainer();
        AddChild(_repairs);
        _feedback = Palette.Label("", 12, Palette.Muted);
        _feedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_feedback);
        c.Changed += RefreshRepairs;
    }

    private void Add(HBoxContainer row, string label, Func<Cascade.Sim.Scheduling.Order> make)
    {
        var b = new Button { Text = label };
        b.Pressed += () => Send(make(), $"{label}: ordered.");
        row.AddChild(b);
    }

    private void Send(Cascade.Sim.Scheduling.Order o, string note)
    {
        _c.SendOrder(o);
        _feedback.Text = $"{note} It takes effect as the clock moves; refusals appear in the log.";
    }

    private string _shown = "";

    private void RefreshRepairs()
    {
        var w = _c.Sim.World;
        int me = w.Nations.Player;
        var damaged = Enumerable.Range(0, w.Substations.Count)
            .Where(s => w.Substations.State[s] == (int)SubstationState.Damaged && w.Provinces.Owner[w.Substations.Province[s]] == me).ToList();
        var key = string.Join(",", damaged.Select(s => $"{s}:{w.Substations.Repair[s]}:{w.Substations.MobileAssigned[s]}")) + $"|{w.Nations.SpareTransformers[me]}|{w.Nations.MobileSubstations[me]}";
        if (key == _shown) return;
        _shown = key;
        foreach (var child in _repairs.GetChildren()) child.QueueFree();
        if (damaged.Count == 0) { _repairs.AddChild(Palette.Label("  none", 12, Palette.Muted)); return; }
        _repairs.AddChild(Palette.Label($"  in reserve: {w.Nations.SpareTransformers[me]} spare, {w.Nations.MobileSubstations[me]} mobile", 12, Palette.Muted));
        foreach (int s in damaged)
        {
            var row = new HBoxContainer();
            row.AddChild(Palette.Label($"  {w.Substations.Keys[s]}{(w.Substations.MobileAssigned[s] ? " (mobile)" : "")}{((RepairKind)w.Substations.Repair[s] != RepairKind.None ? " (repairing)" : "")}", 12));
            int sub = s;
            var spare = new Button { Text = "Spare", TooltipText = "Spare transformer: full capacity in 14 days of work." };
            spare.Pressed += () => Send(new RepairSubstationOrder(me, sub, RepairChoice.Spare), $"Spare transformer to {w.Substations.Keys[sub]}.");
            var mobile = new Button { Text = "Mobile", TooltipText = "Mobile unit: 30% capacity in 7 days." };
            mobile.Pressed += () => Send(new RepairSubstationOrder(me, sub, RepairChoice.Mobile), $"Mobile unit to {w.Substations.Keys[sub]}.");
            row.AddChild(spare);
            row.AddChild(mobile);
            _repairs.AddChild(row);
        }
    }
}

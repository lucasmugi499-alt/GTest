using Cascade.Sim;
using Cascade.Sim.Conflict;
using Cascade.Sim.Economy;
using Cascade.Sim.World; // enum types only; state comes from the snapshot
using Godot;

/// <summary>
/// The main levers outside storylets (placeholder): repairs, mobilization, emergency, sweep, patch, attack.
/// Everything shown comes from the snapshot; buttons send orders with the ids the snapshot gives.
/// </summary>
public partial class OrdersPanel : VBoxContainer
{
    private SimController _c = null!;
    private VBoxContainer _repairs = null!;
    private Label _feedback = null!;

    public void Bind(SimController c)
    {
        _c = c;
        var s = c.Snapshot;
        AddChild(Palette.Label("ORDERS", 13, Palette.Muted));
        int me() => _c.Snapshot.Player;

        var mob = new HBoxContainer();
        mob.AddChild(Palette.Label("Mobilize:", 13));
        var reservists = s.Politics.ReservistsByLevel;
        for (int level = 0; level < reservists.Count; level++)
        {
            int l = level;
            var b = new Button { Text = $"{l}", TooltipText = l == 0 ? "Peacetime" : reservists[l] > 0 ? $"Level {l}: {reservists[l]:N0} reservists called up" : $"Level {l}: readiness, no call-up" };
            b.Pressed += () => Send(new SetMobilizationOrder(me(), l), $"Mobilization level {l} ordered.");
            mob.AddChild(b);
        }
        AddChild(mob);

        var row = new HBoxContainer();
        Add(row, "Declare emergency", () => new DeclareEmergencyOrder(me()));
        Add(row, "End emergency", () => new EndEmergencyOrder(me()));
        AddChild(row);

        var sweep = new HBoxContainer();
        sweep.AddChild(Palette.Label("Forensic sweep:", 13));
        foreach (var p in s.Provinces.Where(p => p.Owner == s.PlayerKey))
        {
            int id = p.Index;
            Add(sweep, p.Name, () => new ForensicSweepOrder(me(), id), $"Sweep {p.Name}'s grid control systems for intruders.");
        }
        AddChild(sweep);

        var row2 = new HBoxContainer();
        foreach (var d in s.Designs)
        {
            int id = d.Index;
            Add(row2, $"Patch {d.Name}", () => new FirmwarePatchOrder(me(), id));
        }
        Add(row2, $"Attack at {s.Front.ProvinceName}", () => new FrontAttackOrder(me()));
        AddChild(row2);

        AddChild(Palette.Label("Damaged substations:", 13, Palette.Muted));
        _repairs = new VBoxContainer();
        AddChild(_repairs);
        _feedback = Palette.Label("", 12, Palette.Muted);
        _feedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_feedback);
        c.Changed += RefreshRepairs;
    }

    private void Add(HBoxContainer row, string label, Func<Cascade.Sim.Scheduling.Order> make, string? tooltip = null)
    {
        var b = new Button { Text = label, TooltipText = tooltip ?? "" };
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
        var s = _c.Snapshot;
        int me = s.Player;
        var damaged = s.Substations.Where(x => x.State == nameof(SubstationState.Damaged) && x.Owner == s.PlayerKey).ToList();
        var key = string.Join(",", damaged.Select(x => $"{x.Index}:{x.Repair}:{x.MobileUnit}")) + $"|{s.Politics.SpareTransformers}|{s.Politics.MobileUnits}";
        if (key == _shown) return;
        _shown = key;
        foreach (var child in _repairs.GetChildren()) child.QueueFree();
        if (damaged.Count == 0) { _repairs.AddChild(Palette.Label("  none", 12, Palette.Muted)); return; }
        _repairs.AddChild(Palette.Label($"  in reserve: {s.Politics.SpareTransformers} spare, {s.Politics.MobileUnits} mobile", 12, Palette.Muted));
        foreach (var x in damaged)
        {
            var row = new HBoxContainer();
            row.AddChild(Palette.Label($"  {x.Id}{(x.MobileUnit ? " (mobile)" : "")}{(x.Repair != nameof(RepairKind.None) ? " (repairing)" : "")}", 12));
            int sub = x.Index;
            string name = x.Id;
            var spare = new Button { Text = "Spare", TooltipText = "Spare transformer: full capacity after the repair crews finish." };
            spare.Pressed += () => Send(new RepairSubstationOrder(me, sub, RepairChoice.Spare), $"Spare transformer to {name}.");
            var mobile = new Button { Text = "Mobile", TooltipText = "Mobile unit: partial capacity within days." };
            mobile.Pressed += () => Send(new RepairSubstationOrder(me, sub, RepairChoice.Mobile), $"Mobile unit to {name}.");
            row.AddChild(spare);
            row.AddChild(mobile);
            _repairs.AddChild(row);
        }
    }
}

using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.World;

/// <summary>Skilled labour pools per province (concept: People as inputs). Mobilization drains them from M3.</summary>
public sealed class LabourStore : IStateHashable, ICommittable
{
    private readonly List<string> _pools;
    public IReadOnlyList<string> Pools => _pools;
    public int PoolCount => Pools.Count;
    private readonly Fixed[] _base;
    /// <summary>People available now, [province × pools + pool].</summary>
    public Column<Fixed> Available { get; }

    public LabourStore(ScenarioDef s, IReadOnlyList<string> extraPools)
    {
        var pools = new List<string>();
        foreach (var name in s.Provinces.SelectMany(p => p.Labour.Select(l => l.Pool)).Concat(extraPools))
            if (!pools.Contains(name)) pools.Add(name);
        _pools = pools;
        _base = new Fixed[s.Provinces.Count * pools.Count];
        Available = new Column<Fixed>("labour.available", _base.Length);
        for (int p = 0; p < s.Provinces.Count; p++)
            foreach (var (pool, people) in s.Provinces[p].Labour)
            {
                int i = p * pools.Count + pools.IndexOf(pool);
                _base[i] = people;
                Available.Init(i, people);
            }
    }

    public int PoolId(string name)
    {
        int i = _pools.IndexOf(name);
        return i >= 0 ? i : throw new KeyNotFoundException($"No labour pool '{name}'.");
    }

    public int Index(int province, int pool) => province * PoolCount + pool;
    public Fixed Base(int province, int pool) => _base[Index(province, pool)];

    /// <summary>Available ÷ base for one pool in one province (1 if the province has none of that pool on its books).</summary>
    public Fine Ratio(int province, int pool)
    {
        var b = Base(province, pool);
        if (b <= Fixed.Zero) return Fine.One;
        return Fine.Min(Fine.One, (Available[Index(province, pool)] / b).ToFine());
    }

    public void Commit() => Available.Commit();
    public void HashInto(StateHasher h) => h.Add(Available);
}

/// <summary>Stockpiles, burn and Days of Cover (spec Stockpiles, Days of Cover and doctrine).</summary>
public sealed class StockStore : IStateHashable, ICommittable
{
    public int Goods { get; }
    public int Provinces { get; }
    public int Nations { get; }

    /// <summary>[province × goods + good]</summary>
    public Column<Fixed> Stock { get; }
    /// <summary>Consumed today: facility inputs (phase 3) plus final demand (phase 5).</summary>
    public Column<Fixed> BurnToday { get; }
    /// <summary>EMA of daily burn, half-life from balance (spec DoC formula).</summary>
    public Column<Fixed> BurnEma { get; }
    /// <summary>Stock set aside in phase 3 for final demand, consumed in phase 5.</summary>
    public Column<Fixed> Reserved { get; }

    /// <summary>[nation × goods + good]. Days of Cover, or -1 when nothing is being burned.</summary>
    public Column<Fixed> DaysOfCover { get; }
    /// <summary>[nation × goods + good]. 0 fine, 1 warning (below replacement lead time), 2 critical (below 14 days).</summary>
    public Column<int> Shortage { get; }

    private readonly ICommittable[] _cols;

    public StockStore(int provinces, int nations, int goods)
    {
        Goods = goods; Provinces = provinces; Nations = nations;
        Stock = new("stock.stock", provinces * goods);
        BurnToday = new("stock.burn_today", provinces * goods);
        BurnEma = new("stock.burn_ema", provinces * goods);
        Reserved = new("stock.reserved", provinces * goods);
        DaysOfCover = new("stock.doc", nations * goods);
        Shortage = new("stock.shortage", nations * goods);
        _cols = [Stock, BurnToday, BurnEma, Reserved, DaysOfCover, Shortage];
    }

    public int At(int province, int good) => province * Goods + good;
    public int AtNation(int nation, int good) => nation * Goods + good;

    public void Commit() { foreach (var c in _cols) c.Commit(); }
    public void HashInto(StateHasher h) { foreach (var c in _cols) h.Add((IStateHashable)c); }
}

public sealed class FacilityStore : Store
{
    public IReadOnlyList<string> Names { get; }
    public int[] Province { get; }
    public string[] Owner { get; }
    public string[] Kind { get; }
    public bool[] DualUse { get; }
    /// <summary>The grid load this facility draws through, or -1 if it uses no power.</summary>
    public int[] Load { get; }
    /// <summary>Labour required: (pool id, people) per facility.</summary>
    public IReadOnlyList<(int Pool, Fixed People)>[] LabourRequired { get; }

    public Column<int> Recipe { get; }
    public Column<Fixed> Capacity { get; }
    public Column<Fixed> Efficiency { get; }
    public Column<Fixed> Damage { get; }
    /// <summary>Player priority override (a <see cref="PriorityTier"/>), or -1 for the default by kind.</summary>
    public Column<int> TierOverride { get; }
    /// <summary>Units of the recipe run today (Q; wafer starts for a fab).</summary>
    public Column<Fixed> RunToday { get; }
    /// <summary>Units of the recipe's first output produced today.</summary>
    public Column<Fixed> OutputToday { get; }

    // Fab state (spec Fab yield). Unused for other lines.
    public Column<Fixed> Experience { get; }
    public Column<Fixed> Yield { get; }
    /// <summary>Days of full power since the last interruption; output ramps as RampDone ÷ ramp days.</summary>
    public Column<int> RampDone { get; }
    public Column<Fixed> WipScrapped { get; }
    public Column<int> Interruptions { get; }
    public Column<Fine> EngineerRatioPrev { get; }

    public FacilityStore(ScenarioDef s, Catalog catalog, ProvinceStore provinces, LabourStore labour, int rampDays)
        : base("facility", s.Facilities.Select(f => f.Id).ToList())
    {
        Names = s.Facilities.Select(f => f.Name).ToList();
        Province = s.Facilities.Select(f => provinces.IdOf(f.Province)).ToArray();
        Owner = s.Facilities.Select(f => f.Owner).ToArray();
        Kind = s.Facilities.Select(f => f.Kind).ToArray();
        DualUse = s.Facilities.Select(f => f.DualUse).ToArray();
        Load = new int[Count];
        Array.Fill(Load, -1);
        LabourRequired = s.Facilities.Select(f => (IReadOnlyList<(int, Fixed)>)f.Labour.Select(l => (labour.PoolId(l.Pool), l.People)).ToList()).ToArray();

        Recipe = Col<int>("recipe");
        Capacity = Col<Fixed>("capacity");
        Efficiency = Col<Fixed>("efficiency");
        Damage = Col<Fixed>("damage");
        TierOverride = Col<int>("tier_override");
        RunToday = Col<Fixed>("run_today");
        OutputToday = Col<Fixed>("output_today");
        Experience = Col<Fixed>("experience");
        Yield = Col<Fixed>("yield");
        RampDone = Col<int>("ramp_done");
        WipScrapped = Col<Fixed>("wip_scrapped");
        Interruptions = Col<int>("interruptions");
        EngineerRatioPrev = Col<Fine>("engineer_ratio_prev");

        for (int i = 0; i < Count; i++)
        {
            var f = s.Facilities[i];
            Recipe.Init(i, catalog.Recipe(f.Recipe));
            Capacity.Init(i, f.Capacity);
            Efficiency.Init(i, f.Efficiency);
            Damage.Init(i, f.Damage);
            TierOverride.Init(i, -1);
            Experience.Init(i, f.ExperienceDays);
            RampDone.Init(i, rampDays);
            EngineerRatioPrev.Init(i, Fine.One);
        }
    }
}

/// <summary>Anything drawing power: facilities' own loads plus homes, hospitals, water, cell towers, data centres.</summary>
public sealed class LoadStore : Store
{
    public int[] Province { get; }
    public int[] Substation { get; }
    public string[] Kind { get; }
    public Fixed[] DemandMw { get; }
    public long[] Population { get; }
    /// <summary>The facility behind this load, or -1.</summary>
    public int[] Facility { get; }
    /// <summary>Backup generator tank size in hours of running (0: no backup).</summary>
    public Fixed[] TankHours { get; }
    public Fixed[] DieselPerHour { get; }

    public Column<int> TierOverride { get; }
    /// <summary>Share of demand served in the most recent dispatch (an hour in a crisis, else the day).</summary>
    public Column<Fine> ServedLast { get; }
    /// <summary>Sum of hourly served shares so far today (crisis provinces only).</summary>
    public Column<Fine> PoweredHours { get; }
    /// <summary>P: the share of the day this load was powered (spec Dispatch).</summary>
    public Column<Fine> PowerRatio { get; }
    /// <summary>True if power was interrupted at any point today (a fab scraps its work in process).</summary>
    public Column<bool> Interrupted { get; }
    /// <summary>Hours of backup fuel left in the tank (spec Blackout clocks).</summary>
    public Column<Fixed> FuelHours { get; }
    public Column<Fine> ServiceHours { get; }
    /// <summary>Share of the day the service ran, on grid or backup.</summary>
    public Column<Fine> ServiceAvailability { get; }

    public LoadStore(ScenarioDef s, ProvinceStore provinces, SubstationStore subs, FacilityStore facilities, GridBalance grid)
        : base("load", s.Loads.Select(l => l.Id).Concat(s.Facilities.Where(f => f.PowerMw > Fixed.Zero).Select(f => f.Id)).ToList())
    {
        Province = new int[Count]; Substation = new int[Count]; Kind = new string[Count];
        DemandMw = new Fixed[Count]; Population = new long[Count]; Facility = new int[Count];
        TankHours = new Fixed[Count]; DieselPerHour = new Fixed[Count];
        Array.Fill(Facility, -1);

        int i = 0;
        foreach (var l in s.Loads)
        {
            Substation[i] = subs.IdOf(l.Substation);
            Province[i] = subs.Province[Substation[i]];
            Kind[i] = l.Kind;
            DemandMw[i] = l.DemandMw;
            Population[i] = l.Population;
            DieselPerHour[i] = l.DieselPerHour;
            if (l.DieselPerHour > Fixed.Zero)
            {
                TankHours[i] = l.TankHours
                    ?? (grid.BackupTankHours.TryGetValue(l.Kind, out var t) ? t
                        : throw new ContentException($"Load '{l.Id}' has backup but no tank_hours, and '{l.Kind}' has no default in grid.backup_tank_hours."));
            }
            i++;
        }
        for (int f = 0; f < s.Facilities.Count; f++)
        {
            var fd = s.Facilities[f];
            if (fd.PowerMw <= Fixed.Zero) continue;
            Substation[i] = subs.IdOf(fd.Substation!);
            Province[i] = provinces.IdOf(fd.Province);
            Kind[i] = fd.Kind;
            DemandMw[i] = fd.PowerMw;
            Facility[i] = f;
            facilities.Load[f] = i;
            i++;
        }

        TierOverride = Col<int>("tier_override");
        ServedLast = Col<Fine>("served_last");
        PoweredHours = Col<Fine>("powered_hours");
        PowerRatio = Col<Fine>("power_ratio");
        Interrupted = Col<bool>("interrupted");
        FuelHours = Col<Fixed>("fuel_hours");
        ServiceHours = Col<Fine>("service_hours");
        ServiceAvailability = Col<Fine>("service_availability");
        for (int k = 0; k < Count; k++)
        {
            TierOverride.Init(k, -1);
            ServedLast.Init(k, Fine.One);
            PowerRatio.Init(k, Fine.One);
            ServiceAvailability.Init(k, Fine.One);
            FuelHours.Init(k, TankHours[k]);
        }
    }

    public bool HasBackup(int load) => TankHours[load] > Fixed.Zero;
}

/// <summary>Damaged needs a repair; Tripped comes back by itself when a cyber disruption ends (spec Grid disruption).</summary>
public enum SubstationState { Online = 0, Damaged = 1, Tripped = 2 }
public enum RepairKind { None = 0, Spare = 1, NewTransformer = 2 }

/// <summary>Substations and their repair jobs (spec Substations).</summary>
public sealed class SubstationStore : Store
{
    public int[] Province { get; }
    public Fixed[] CapacityMw { get; }

    public Column<int> State { get; }
    /// <summary>A mobile emergency unit is on site (in progress or delivering its 30%).</summary>
    public Column<bool> MobileAssigned { get; }
    public Column<Fixed> MobileProgress { get; }
    /// <summary>The full repair under way: a <see cref="RepairKind"/>.</summary>
    public Column<int> Repair { get; }
    public Column<Fixed> RepairProgress { get; }
    public Column<Fixed> RepairRequired { get; }
    /// <summary>Hours left on a cyber disruption (state Tripped).</summary>
    public Column<Fixed> TripHoursLeft { get; }

    public SubstationStore(ScenarioDef s, ProvinceStore provinces) : base("substation", s.Substations.Select(x => x.Id).ToList())
    {
        Province = s.Substations.Select(x => provinces.IdOf(x.Province)).ToArray();
        CapacityMw = s.Substations.Select(x => x.CapacityMw).ToArray();
        State = Col<int>("state");
        MobileAssigned = Col<bool>("mobile_assigned");
        MobileProgress = Col<Fixed>("mobile_progress");
        Repair = Col<int>("repair");
        RepairProgress = Col<Fixed>("repair_progress");
        RepairRequired = Col<Fixed>("repair_required");
        TripHoursLeft = Col<Fixed>("trip_hours_left");
    }

    /// <summary>Share of nameplate capacity available: 1 online, 0 damaged, the mobile unit's share once it's installed.</summary>
    public Fine CapacityFactor(int id, GridBalance g)
    {
        if (State[id] == (int)SubstationState.Online) return Fine.One;
        if (MobileAssigned[id] && MobileProgress[id] >= Fixed.FromInt(g.MobileUnitDays)) return g.MobileUnitCapacity;
        return Fine.Zero;
    }
}

public sealed class PlantStore : Store
{
    public int[] Province { get; }
    public Fixed[] CapacityMw { get; }
    public bool[] BlackStart { get; }
    /// <summary>Share of capacity available (damage to plants comes with later milestones).</summary>
    public Column<Fine> Available { get; }

    public PlantStore(ScenarioDef s, ProvinceStore provinces) : base("plant", s.Plants.Select(x => x.Id).ToList())
    {
        Province = s.Plants.Select(x => provinces.IdOf(x.Province)).ToArray();
        CapacityMw = s.Plants.Select(x => x.CapacityMw).ToArray();
        BlackStart = s.Plants.Select(x => x.BlackStart).ToArray();
        Available = Col<Fine>("available");
        for (int i = 0; i < Count; i++) Available.Init(i, Fine.One);
    }
}

public sealed class TieLineStore : Store
{
    public int[] A { get; }
    public int[] B { get; }
    public Fixed[] CapacityMw { get; }
    /// <summary>MW flowing from A to B in the last dispatch (negative: B to A).</summary>
    public Column<Fixed> Flow { get; }

    public TieLineStore(ScenarioDef s, ProvinceStore provinces)
        : base("tie_line", s.TieLines.Select(t => $"{t.A}~{t.B}").ToList())
    {
        A = s.TieLines.Select(t => provinces.IdOf(t.A)).ToArray();
        B = s.TieLines.Select(t => provinces.IdOf(t.B)).ToArray();
        CapacityMw = s.TieLines.Select(t => t.CapacityMw).ToArray();
        Flow = Col<Fixed>("flow");
    }
}

/// <summary>Domestic transport edges, usable both ways (D-007).</summary>
public sealed class EdgeStore : Store
{
    public int[] A { get; }
    public int[] B { get; }
    public int[] LeadDays { get; }
    /// <summary>Tonnes per day by <see cref="TransportClass"/>, [edge × 4 + class].</summary>
    public Fixed[] CapacityTonnes { get; }

    public EdgeStore(ScenarioDef s, ProvinceStore provinces) : base("edge", s.Edges.Select(e => $"{e.A}~{e.B}").ToList())
    {
        A = s.Edges.Select(e => provinces.IdOf(e.A)).ToArray();
        B = s.Edges.Select(e => provinces.IdOf(e.B)).ToArray();
        LeadDays = s.Edges.Select(e => e.LeadDays).ToArray();
        CapacityTonnes = s.Edges.SelectMany(e => e.CapacityTonnesByClass).ToArray();
    }
}

public sealed class ImportStore : Store
{
    public int[] Good { get; }
    /// <summary>Source nation id, or -1 for the world market.</summary>
    public int[] Source { get; }
    public int[] To { get; }
    public int[] LeadDays { get; }
    public Fixed[] CapacityPerDay { get; }
    public bool[] Sea { get; }

    /// <summary>Stopped by the source's export controls (spec Supply shock).</summary>
    public Column<bool> Blocked { get; }
    public Column<Fixed> ShippedToday { get; }
    /// <summary>Share of capacity still available: sea routes lose shipping lines that skip the port (spec War-risk insurance).</summary>
    public Column<Fine> CapacityFactor { get; }

    public ImportStore(ScenarioDef s, Catalog catalog, NationStore nations, ProvinceStore provinces)
        : base("import", s.Imports.Select(i => $"{i.Good}<{i.From}").ToList())
    {
        Good = s.Imports.Select(i => catalog.Good(i.Good)).ToArray();
        Source = s.Imports.Select(i => i.From == ScenarioDef.WorldSource ? -1 : nations.IdOf(i.From)).ToArray();
        To = s.Imports.Select(i => provinces.IdOf(i.To)).ToArray();
        LeadDays = s.Imports.Select(i => i.LeadDays).ToArray();
        CapacityPerDay = s.Imports.Select(i => i.CapacityPerDay).ToArray();
        Sea = s.Imports.Select(i => i.Sea).ToArray();
        Blocked = Col<bool>("blocked");
        ShippedToday = Col<Fixed>("shipped_today");
        CapacityFactor = Col<Fine>("capacity_factor");
        for (int i = 0; i < Count; i++) CapacityFactor.Init(i, Fine.One);
    }
}

/// <summary>Final demand: consumption that isn't a facility's input (civil industry, households, transport).</summary>
public sealed class DemandStore : Store
{
    public int[] Province { get; }
    public int[] Good { get; }
    public Fixed[] PerDay { get; }
    public string[] Kind { get; }

    public Column<int> TierOverride { get; }
    public Column<Fixed> ReservedToday { get; }
    public Column<Fixed> ServedToday { get; }

    public DemandStore(ScenarioDef s, Catalog catalog, ProvinceStore provinces)
        : base("demand", s.Demand.Select((d, i) => $"{d.Province}:{d.Good}:{i}").ToList())
    {
        Province = s.Demand.Select(d => provinces.IdOf(d.Province)).ToArray();
        Good = s.Demand.Select(d => catalog.Good(d.Good)).ToArray();
        PerDay = s.Demand.Select(d => d.PerDay).ToArray();
        Kind = s.Demand.Select(d => d.Kind).ToArray();
        TierOverride = Col<int>("tier_override");
        ReservedToday = Col<Fixed>("reserved_today");
        ServedToday = Col<Fixed>("served_today");
        for (int i = 0; i < Count; i++) TierOverride.Init(i, -1);
    }
}

/// <summary>Weapon designs and their effectiveness against the rival's adapting EW (spec Countermeasure decay).</summary>
public sealed class DesignStore : Store
{
    public Column<Fixed> Effectiveness { get; }
    public Column<Fixed> Cap { get; }

    public DesignStore(Catalog catalog) : base("design", catalog.Designs.Select(d => d.Key).ToList())
    {
        Effectiveness = Col<Fixed>("effectiveness");
        Cap = Col<Fixed>("cap");
        foreach (var d in catalog.Designs)
        {
            Effectiveness.Init(d.Id, d.Effectiveness);
            Cap.Init(d.Id, d.Cap);
        }
    }
}

/// <summary>A consignment on its way. Imports have From = -1.</summary>
public readonly record struct Shipment(long Seq, int Good, int From, int To, Fixed Qty, int ArriveDay);

/// <summary>
/// Goods in transit, kept in (arrival day, sequence) order. Like the event queue, this is a list that phases
/// append to and remove from directly rather than a double-buffered column: only phase 4 adds, only phase 1 delivers.
/// </summary>
public sealed class ShipmentList : IStateHashable
{
    private readonly List<Shipment> _items = [];
    private long _nextSeq;

    public IReadOnlyList<Shipment> Items => _items;

    public void Add(int good, int from, int to, Fixed qty, int arriveDay)
    {
        var s = new Shipment(_nextSeq++, good, from, to, qty, arriveDay);
        int i = _items.Count;
        while (i > 0 && _items[i - 1].ArriveDay > arriveDay) i--;
        _items.Insert(i, s);
    }

    public List<Shipment> TakeDue(int day)
    {
        int n = 0;
        while (n < _items.Count && _items[n].ArriveDay <= day) n++;
        var due = _items.GetRange(0, n);
        _items.RemoveRange(0, n);
        return due;
    }

    public void HashInto(StateHasher h)
    {
        h.Section("shipments").Add(_nextSeq).Add(_items.Count);
        foreach (var s in _items) h.Add(s.Seq).Add(s.Good).Add(s.From).Add(s.To).Add(s.Qty).Add(s.ArriveDay);
    }
}

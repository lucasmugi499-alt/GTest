using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.World;

/// <summary>All live sim state. Only phases (via the scheduler) write to it; the UI reads snapshots.</summary>
public sealed class SimWorld : ICommittable, IStateHashable
{
    public Catalog Catalog { get; }

    public NationStore Nations { get; }
    public ProvinceStore Provinces { get; }
    public LabourStore Labour { get; }
    public StockStore Stocks { get; }
    public FacilityStore Facilities { get; }
    public SubstationStore Substations { get; }
    public LoadStore Loads { get; }
    public PlantStore Plants { get; }
    public TieLineStore TieLines { get; }
    public EdgeStore Edges { get; }
    public ImportStore Imports { get; }
    public DemandStore Demand { get; }
    public DesignStore Designs { get; }
    public ShipmentList Shipments { get; } = new();

    private readonly ICommittable[] _commit;
    private readonly IStateHashable[] _hash;

    public SimWorld(ContentSet content)
    {
        var s = content.Scenario;
        var b = content.Balance;
        Catalog = content.Catalog;
        Nations = new NationStore(s);
        Provinces = new ProvinceStore(s, Nations);
        Labour = new LabourStore(s, s.Facilities.SelectMany(f => f.Labour.Select(l => l.Pool)).ToList());
        Stocks = new StockStore(Provinces.Count, Nations.Count, Catalog.Goods.Count);
        Facilities = new FacilityStore(s, Catalog, Provinces, Labour, b.Fab.RampDays);
        Substations = new SubstationStore(s, Provinces);
        Loads = new LoadStore(s, Provinces, Substations, Facilities, b.Grid);
        Plants = new PlantStore(s, Provinces);
        TieLines = new TieLineStore(s, Provinces);
        Edges = new EdgeStore(s, Provinces);
        Imports = new ImportStore(s, Catalog, Nations, Provinces);
        Demand = new DemandStore(s, Catalog, Provinces);
        Designs = new DesignStore(Catalog);

        // Fixed order: this is the order state commits and hashes in.
        _commit = [Nations, Provinces, Labour, Stocks, Facilities, Substations, Loads, Plants, TieLines, Edges, Imports, Demand, Designs];
        _hash = [.. _commit.Cast<IStateHashable>(), Shipments];
    }

    public void Commit()
    {
        foreach (var s in _commit) s.Commit();
    }

    public void HashInto(StateHasher h)
    {
        foreach (var s in _hash) s.HashInto(h);
    }
}

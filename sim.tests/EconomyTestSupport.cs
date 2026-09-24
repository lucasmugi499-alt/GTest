using Cascade.Sim.Economy;
using Cascade.Sim.Scheduling;

namespace Cascade.Sim.Tests;

internal static class Veyl
{
    public static readonly string[] AttackedSubstations = ["ossen_north", "ossen_south", "ossen_industrial"];

    /// <summary>Damages the three Ossen East substations at the given day and hour (the Day 4 02:14 attack by default).</summary>
    public static void TripOssen(Simulation sim, int day = 4, int hour = 2, params string[] substations)
    {
        var w = sim.World;
        var ids = (substations.Length == 0 ? AttackedSubstations : substations).Select(w.Substations.IdOf).ToList();
        sim.Events.Schedule(new SubstationDamageEvent(day, hour, w.Provinces.IdOf("kestria_east_ossen"), ids));
    }

    /// <summary>National legacy chip output yesterday (what the fabs produced on the last completed day).</summary>
    public static double ChipOutput(Simulation sim) => SimSnapshot.Of(sim).Good("legacy_chip").ProducedToday;

    public static int Load(Simulation sim, string key) => sim.World.Loads.IdOf(key);
    public static int Facility(Simulation sim, string key) => sim.World.Facilities.IdOf(key);
    public static int Sub(Simulation sim, string key) => sim.World.Substations.IdOf(key);
    public static int Province(Simulation sim, string key) => sim.World.Provinces.IdOf(key);
    public static int Good(Simulation sim, string key) => sim.Content.Catalog.Good(key);
}

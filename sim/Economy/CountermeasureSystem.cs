using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;

namespace Cascade.Sim.Economy;

/// <summary>
/// Spec Countermeasure decay, weekly: e_{w+1} = e_w (1 − δA). The cap a firmware patch can restore decays
/// too, at a share of that rate, so that hardware revisions matter (D-030).
/// </summary>
public sealed class CountermeasureSystem : IPeriodicSystem
{
    public SystemId Id => SystemId.WeeklyCountermeasures;

    public void Run(TickContext ctx)
    {
        var c = ctx.Balance.Countermeasures;
        var d = ctx.World.Designs;
        var decay = c.DecayPerWeek * c.Adaptation;
        for (int i = 0; i < d.Count; i++)
        {
            d.Effectiveness.Set(i, d.Effectiveness[i] * (Fixed.One - decay));
            d.Cap.Set(i, d.Cap[i] * (Fixed.One - decay * c.CapDecayShare));
        }
    }
}

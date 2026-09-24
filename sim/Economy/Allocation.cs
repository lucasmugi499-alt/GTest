using Cascade.Sim.Content;
using Cascade.Sim.Core;

namespace Cascade.Sim.Economy;

/// <summary>
/// Spec Shortage allocation: tiers fill in order (Critical, High, Normal, Low); the tier where supply runs
/// out is shared proportionally: a_j = d_j · min(1, R_k / D_k).
/// Shares round down, so the total handed out never exceeds the supply.
/// </summary>
public static class Allocation
{
    public static void ByTier(Fixed supply, ReadOnlySpan<Fixed> demand, ReadOnlySpan<PriorityTier> tier, Span<Fixed> result)
    {
        if (demand.Length != tier.Length || demand.Length != result.Length) throw new ArgumentException("Length mismatch.");
        long remaining = Math.Max(0, supply.Raw);
        for (int k = 0; k < PriorityTiers.Count; k++)
        {
            long tierDemand = 0;
            for (int j = 0; j < demand.Length; j++)
                if ((int)tier[j] == k) tierDemand = checked(tierDemand + Math.Max(0, demand[j].Raw));

            if (tierDemand <= remaining)
            {
                for (int j = 0; j < demand.Length; j++)
                    if ((int)tier[j] == k) result[j] = Fixed.FromRaw(Math.Max(0, demand[j].Raw));
                remaining -= tierDemand;
                continue;
            }

            for (int j = 0; j < demand.Length; j++)
            {
                if ((int)tier[j] != k) continue;
                // floor(d_j × R_k / D_k)
                result[j] = Fixed.FromRaw((long)((Int128)Math.Max(0, demand[j].Raw) * remaining / tierDemand));
            }
            // Lower tiers get nothing.
            for (int j = 0; j < demand.Length; j++)
                if ((int)tier[j] > k) result[j] = Fixed.Zero;
            return;
        }
    }
}

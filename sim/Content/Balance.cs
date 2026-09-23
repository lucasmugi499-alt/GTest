namespace Cascade.Sim.Content;

/// <summary>
/// Every tunable number in the game, read from content/balance.yaml. Code never holds a tunable literal;
/// it reads it from here. Sections grow milestone by milestone.
/// </summary>
public sealed record Balance(SimBalance Sim)
{
    public static Balance Read(ContentNode root) => new(SimBalance.Read(root.Child("sim")));
}

public sealed record SimBalance(CrisisBalance Crisis, int HashCheckIntervalDays)
{
    public static SimBalance Read(ContentNode n) => new(
        CrisisBalance.Read(n.Child("crisis")),
        n.Child("state_hash").Int("check_interval_days"));
}

/// <summary>Spec: Crisis sub-ticks.</summary>
public sealed record CrisisBalance(int ClearAfterStableHours)
{
    public static CrisisBalance Read(ContentNode n) => new(n.Int("clear_after_stable_hours"));
}

namespace Cascade.Sim.Core;

/// <summary>
/// Counter-based randomness (spec Conventions): every roll is hash(seed, tick, system, entity, roll index).
/// No hidden state, so the order systems or threads run in can never change a result.
/// </summary>
public static class Rng
{
    /// <summary>A 64-bit hash of the five roll coordinates, built from the SplitMix64 finalizer (a bijection per step).</summary>
    public static ulong Hash(ulong seed, ulong tick, ulong system, ulong entity, ulong index)
    {
        ulong h = Mix(seed ^ 0x6A09E667F3BCC908UL);
        h = Mix(h ^ tick);
        h = Mix(h ^ system);
        h = Mix(h ^ entity);
        h = Mix(h ^ index);
        return h;
    }

    internal static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// The "tick" coordinate: day × 32 + slot. Slots 0–23 are crisis hours, then daily, weekly and monthly (D-017).
    /// </summary>
    public static ulong TickKey(int day, TickSlot slot) => (ulong)day * 32 + (ulong)slot;
}

/// <summary>Which pass of a day a roll belongs to. Values are part of the save format: never renumber.</summary>
public enum TickSlot : byte
{
    // 0..23 are hours; cast an hour directly: (TickSlot)hour.
    Daily = 24,
    Weekly = 25,
    Monthly = 26,
}

/// <summary>
/// A stream of rolls for one (seed, tick, system, entity). The roll index counts up locally, so each
/// system/entity pair gets its own independent sequence regardless of what else runs.
/// </summary>
public struct RngStream
{
    private readonly ulong _seed, _tick, _system, _entity;
    private ulong _index;

    public RngStream(ulong seed, ulong tick, ulong system, ulong entity)
    {
        _seed = seed; _tick = tick; _system = system; _entity = entity; _index = 0;
    }

    public ulong NextU64() => Rng.Hash(_seed, _tick, _system, _entity, _index++);

    /// <summary>Uniform in [0, 1) at 8 decimals.</summary>
    public Fine NextFine() => Fine.FromRaw((long)(((UInt128)NextU64() * (ulong)Fine.Scale) >> 64));

    /// <summary>True with probability p.</summary>
    public bool Chance(Fine p) => NextFine() < p;

    /// <summary>Uniform integer in [min, max).</summary>
    public int NextInt(int min, int max)
    {
        if (max <= min) throw new ArgumentOutOfRangeException(nameof(max));
        ulong span = (ulong)(max - min);
        return min + (int)(((UInt128)NextU64() * span) >> 64);
    }

    /// <summary>
    /// Approximately normal (Irwin–Hall: sum of 12 uniforms − 6), mean and standard deviation as given.
    /// Bounded to ±6 standard deviations, which is fine for red lines and noise.
    /// </summary>
    public Fixed NextNormal(Fixed mean, Fixed sd)
    {
        long sum = 0;
        for (int i = 0; i < 12; i++) sum += NextFine().Raw;
        Fine z = Fine.FromRaw(sum - 6 * Fine.Scale);
        return mean + sd.Times(z);
    }
}

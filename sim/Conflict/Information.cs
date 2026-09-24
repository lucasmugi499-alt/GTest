using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.Scheduling;
using Cascade.Sim.World;

namespace Cascade.Sim.Conflict;

/// <summary>
/// Phase 8 (spec Narrative spread). Per narrative and segment, shares S (susceptible), E (exposed), B (believing),
/// R (rejecting) move as:
///   new exposed = β V R_s (1 − T_s/150) S_s Σ w_ss' B_s'
///   dE = new exposed − η E;  dB = η Pl E − γ B;  exposed non-believers → R at η (1 − Pl); fading believers → R.
/// Steps are daily, or hourly on a day when any segment's province is in Crisis Time (D-038).
/// Past 25% belief a segment is "established": γ drops to 0.005 and factions react once.
/// </summary>
public sealed class InformationPhase : IHourlyPhase
{
    public PhaseId Id => PhaseId.Information;

    public void RunHourly(TickContext ctx)
    {
        if (!Information.HourlyToday(ctx)) return;
        Information.Step(ctx.World, ctx.Balance, ctx.Content, ctx.Day, Fine.Ratio(1, 24), write: true);
        Information.UpdateRumorVelocity(ctx);
    }

    public void RunDaily(TickContext ctx)
    {
        if (!Information.HourlyToday(ctx)) Information.Step(ctx.World, ctx.Balance, ctx.Content, ctx.Day, Fine.One, write: true);
        Information.ApplyEstablishment(ctx);
        Information.UpdateRumorVelocity(ctx);
    }
}

public static class Information
{
    public static bool HourlyToday(TickContext ctx)
    {
        foreach (int p in ctx.CrisisProvinces)
            for (int s = 0; s < ctx.World.Segments.Count; s++)
                if (ctx.World.Segments.Province[s] == p) return true;
        return false;
    }

    /// <summary>Seeds a narrative: the given shares of each segment start out believing it.</summary>
    public static void Seed(TickContext ctx, int n, IReadOnlyList<(string Segment, Fine Share)> seed)
    {
        var nar = ctx.World.Narratives;
        nar.Active.Set(n, true);
        if (nar.SeededDay.Pending(n) < 0) nar.SeededDay.Set(n, ctx.Day);
        foreach (var (segKey, share) in seed)
        {
            int at = nar.At(n, ctx.World.Segments.IdOf(segKey));
            var take = Fine.Min(share, nar.S.Pending(at));
            nar.S.Set(at, nar.S.Pending(at) - take);
            nar.B.Set(at, nar.B.Pending(at) + take);
        }
    }

    /// <summary>Effective contact w_ss' after platform takedowns and regional shutdowns (spec counter-measures).</summary>
    public static Fine Contact(SimWorld w, Balance b, ContentSet c, int n, int s, int t)
    {
        var so = c.Scenario.Society;
        if (w.Provinces.InternetShutdown[w.Segments.Province[s]] || w.Provinces.InternetShutdown[w.Segments.Province[t]]) return Fine.Zero;
        var platforms = Fine.Zero;
        foreach (var pl in so.Platforms)
        {
            var weight = pl.Share;
            if (w.Narratives.Takedown[n] && IsTakedownPlatform(so, pl)) weight = weight * b.Information.TakedownWeight;
            platforms += weight;
        }
        return so.Contact[s, t] * platforms;
    }

    /// <summary>Takedowns apply to platforms owned by a corporation (the slice's one: Brightline).</summary>
    private static bool IsTakedownPlatform(SocietyDef so, PlatformDef pl) => pl.Owner != ScenarioDef.None;

    /// <summary>
    /// One Euler step of <paramref name="dt"/> days for every active narrative. With write false it runs on the
    /// arrays passed in (the Rumor Velocity forecast) and leaves the world alone.
    /// </summary>
    public static void Step(SimWorld w, Balance b, ContentSet c, int day, Fine dt, bool write,
        Fine[]? S = null, Fine[]? E = null, Fine[]? B = null, Fine[]? R = null, int only = -1)
    {
        var nar = w.Narratives;
        var info = b.Information;
        int segs = nar.Segments;
        for (int n = 0; n < nar.Count; n++)
        {
            if (only >= 0 && n != only) continue;
            if (!nar.Active[n]) continue;
            var def = nar.Defs[n];
            var virality = def.Virality.ToFine() * ViralityMultiplier(w, c, nar.Origin[n]);
            bool counter = nar.CounterUntil[n] >= day;

            var bNow = new Fine[segs];
            for (int s = 0; s < segs; s++) bNow[s] = write ? nar.B[nar.At(n, s)] : B![nar.At(n, s)];

            for (int s = 0; s < segs; s++)
            {
                int at = nar.At(n, s);
                var sS = write ? nar.S[at] : S![at];
                var sE = write ? nar.E[at] : E![at];
                var sB = bNow[s];
                var sR = write ? nar.R[at] : R![at];

                var force = Fine.Zero;
                for (int t = 0; t < segs; t++) force += Contact(w, b, c, n, s, t) * bNow[t];
                var trustFactor = Fine.One - (w.Segments.Trust[s] / info.TrustDivisor).ToFine();
                var newE = Fine.Min(sS, info.Beta * virality * nar.Resonance[at] * trustFactor * sS * force * dt);
                var prebunk = (nar.PrebunkMask[n] & (1 << s)) != 0 ? Fine.Min(sS - newE, info.PrebunkingPerDay * sS * dt) : Fine.Zero;
                var leaveE = Fine.Min(sE, info.Eta * sE * dt);
                var toB = leaveE * def.Plausibility;
                var toR = leaveE - toB;
                var gamma = nar.Established[at] ? info.GammaEstablished : info.Gamma;
                if (counter) gamma = gamma * b.Information.CounterGammaMultiplier.ToFine();
                var fade = Fine.Min(sB, gamma * sB * dt);

                sS = sS - newE - prebunk;
                sE = sE + newE - leaveE;
                sB = sB + toB - fade;
                sR = sR + toR + fade + prebunk;

                if (write)
                {
                    nar.S.Set(at, sS); nar.E.Set(at, sE); nar.B.Set(at, sB); nar.R.Set(at, sR);
                    if (!nar.Established[at] && sB >= info.EstablishedShare) nar.Established.Set(at, true);
                }
                else { S![at] = sS; E![at] = sE; B![at] = sB; R![at] = sR; }
            }
        }
    }

    private static Fine ViralityMultiplier(SimWorld w, ContentSet c, int origin)
    {
        var m = Fine.One;
        foreach (var (power, i) in c.Scenario.Society.EmergencyPowers.Select((p, i) => (p, i)))
            for (int n = 0; n < w.Nations.Count; n++)
                if (n != origin && (w.Politics.PowersMask[n] & (1 << i)) != 0) m = m * power.ViralityMultiplier;
        return m;
    }

    /// <summary>When a narrative first becomes established in any segment, its faction shifts apply once.</summary>
    public static void ApplyEstablishment(TickContext ctx)
    {
        var w = ctx.World;
        var nar = w.Narratives;
        for (int n = 0; n < nar.Count; n++)
        {
            if (nar.FactionEffectsApplied[n]) continue;
            int first = -1;
            for (int s = 0; s < nar.Segments; s++) if (nar.Established[nar.At(n, s)]) { first = s; break; }
            if (first < 0) continue;
            nar.FactionEffectsApplied.Set(n, true);
            foreach (var (faction, shift) in nar.Defs[n].Established)
            {
                int f = w.Factions.IdOf(faction);
                w.Factions.Standing.Set(f, w.Factions.Standing.Pending(f) + shift);
            }
            w.Log.Add(ctx.Day, ctx.Hour, "narrative", $"\"{nar.Defs[n].Name}\" takes hold among {w.Segments.Defs[first].Name.ToLowerInvariant()}.",
                "", nar.Keys[n]);
        }
    }

    /// <summary>
    /// Rumor Velocity (spec): forecast hours until any segment not yet established crosses 25% belief, stepping a copy
    /// hourly with policies frozen. -1 if not within the horizon.
    /// </summary>
    public static void UpdateRumorVelocity(TickContext ctx)
    {
        var w = ctx.World;
        var nar = w.Narratives;
        var info = ctx.Balance.Information;
        for (int n = 0; n < nar.Count; n++)
        {
            if (!nar.Active.Pending(n)) { nar.RumorHours.Set(n, Fixed.FromInt(-1)); continue; }
            int len = nar.Count * nar.Segments;
            var S = new Fine[len]; var E = new Fine[len]; var B = new Fine[len]; var R = new Fine[len];
            var done = new bool[nar.Segments];
            bool anyOpen = false;
            for (int s = 0; s < nar.Segments; s++)
            {
                int at = nar.At(n, s);
                S[at] = nar.S.Pending(at); E[at] = nar.E.Pending(at); B[at] = nar.B.Pending(at); R[at] = nar.R.Pending(at);
                done[s] = nar.Established.Pending(at) || B[at] >= info.EstablishedShare;
                anyOpen |= !done[s];
            }
            int hours = -1;
            if (anyOpen)
            {
                var hour = Fine.Ratio(1, 24);
                for (int h = 1; h <= info.RumorHorizonHours && hours < 0; h++)
                {
                    Step(w, ctx.Balance, ctx.Content, ctx.Day + h / 24, hour, write: false, S, E, B, R, only: n);
                    for (int s = 0; s < nar.Segments; s++)
                        if (!done[s] && B[nar.At(n, s)] >= info.EstablishedShare) { hours = h; break; }
                    // Stop early once belief is falling everywhere.
                    if (h % 24 == 0 && Enumerable.Range(0, nar.Segments).All(s => E[nar.At(n, s)] < Fine.Ratio(1, 100_000))) break;
                }
            }
            nar.RumorHours.Set(n, Fixed.FromInt(hours));
        }
    }
}

/// <summary>Spec Crisis sub-ticks: a narrative within 12 hours of tipping puts its segments' provinces in Crisis Time.</summary>
public sealed class NarrativeCrisisSignal(Balance balance) : ICrisisSignal
{
    public string Name => "narrative";

    public bool IsUnstable(SimWorld w, int province)
    {
        var nar = w.Narratives;
        var limit = Fixed.FromInt(balance.Information.RumorCrisisHours);
        for (int n = 0; n < nar.Count; n++)
        {
            var h = nar.RumorHours[n];
            if (h < Fixed.Zero || h > limit) continue;
            for (int s = 0; s < w.Segments.Count; s++)
                if (w.Segments.Province[s] == province && !nar.Established[nar.At(n, s)]) return true;
        }
        return false;
    }
}

/// <summary>Spec Crisis sub-ticks: a front breaking through puts its province in Crisis Time.</summary>
public sealed class FrontCrisisSignal : ICrisisSignal
{
    public string Name => "front";

    public bool IsUnstable(SimWorld w, int province)
    {
        if (province != w.Front.Province) return false;
        for (int n = 0; n < w.Nations.Count; n++) if (w.Front.AdvanceToday[n] > Fixed.Zero) return true;
        return false;
    }
}

using Cascade.Sim.Core;

namespace Cascade.Sim.Scheduling;

/// <summary>
/// Something due to happen on a given day, optionally at a given hour and in a given province:
/// a delivery, an operation's effect, a storylet outcome. Applied in phase 1.
/// </summary>
public abstract class SimEvent : IStateHashable
{
    public int Day { get; }
    /// <summary>0–23 for a timed event, or -1 for "some time that day" (applied in the daily pass).</summary>
    public int Hour { get; }
    /// <summary>Province the event lands in, or -1. A timed event in a province puts it in Crisis Time that day (D-019).</summary>
    public int Province { get; }
    public long Seq { get; internal set; } = -1;

    protected SimEvent(int day, int hour = -1, int province = -1)
    {
        if (hour is < -1 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        Day = day;
        Hour = hour;
        Province = province;
    }

    public bool IsTimed => Hour >= 0;

    public abstract string Kind { get; }
    public abstract void Apply(TickContext ctx);
    protected abstract void HashFields(StateHasher h);

    public void HashInto(StateHasher h)
    {
        h.Add(Kind).Add(Seq).Add(Day).Add(Hour).Add(Province);
        HashFields(h);
    }
}

/// <summary>Pending events, always kept in (day, hour, arrival) order. Untimed events sort after hour 23.</summary>
public sealed class EventQueue : IStateHashable
{
    private sealed class DueOrder : IComparer<SimEvent>
    {
        public int Compare(SimEvent? a, SimEvent? b)
        {
            int c = a!.Day.CompareTo(b!.Day);
            if (c != 0) return c;
            c = HourKey(a).CompareTo(HourKey(b));
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        }

        private static int HourKey(SimEvent e) => e.IsTimed ? e.Hour : 24;
    }

    private readonly SortedSet<SimEvent> _events = new(new DueOrder());
    private long _nextSeq;

    public int Count => _events.Count;

    public void Schedule(SimEvent e)
    {
        if (e.Seq >= 0) throw new InvalidOperationException("Event was already scheduled.");
        e.Seq = _nextSeq++;
        _events.Add(e);
    }

    /// <summary>Events due by the given hour of the day (hourly pass), or by the end of the day when hour is null.</summary>
    internal List<SimEvent> TakeDue(int day, int? hour)
    {
        var due = new List<SimEvent>();
        foreach (var e in _events)
        {
            if (e.Day > day) break;
            bool isDue = e.Day < day || hour is null || (e.IsTimed && e.Hour <= hour);
            if (isDue) due.Add(e);
        }
        foreach (var e in due) _events.Remove(e);
        return due;
    }

    internal bool HasTimedEventIn(int day, int province)
    {
        foreach (var e in _events)
        {
            if (e.Day > day) break;
            if (e.Day == day && e.IsTimed && e.Province == province) return true;
        }
        return false;
    }

    public void HashInto(StateHasher h)
    {
        h.Section("events").Add(_nextSeq).Add(_events.Count);
        foreach (var e in _events) e.HashInto(h);
    }
}

using Cascade.Sim.Core;

namespace Cascade.Sim.World;

/// <summary>Anything holding double-buffered state that the scheduler commits after each phase.</summary>
public interface ICommittable
{
    void Commit();
}

/// <summary>
/// One field of one entity type, stored as an array indexed by entity ID (spec: columnar arrays, 32-bit IDs).
///
/// Double-buffered (spec Tick pipeline): a phase reads the committed value with the indexer and writes
/// the next-state value with <see cref="Set"/>. Writes become visible only when the scheduler commits
/// after the phase, so work inside a phase can never see its own partial results.
/// </summary>
public sealed class Column<T> : ICommittable, IStateHashable where T : unmanaged
{
    private readonly T[] _cur;
    private readonly T[] _next;
    private bool _dirty;

    public string Name { get; }

    public Column(string name, int count)
    {
        Name = name;
        _cur = new T[count];
        _next = new T[count];
    }

    public int Count => _cur.Length;

    /// <summary>The committed value (what earlier phases produced).</summary>
    public T this[int id] => _cur[id];

    /// <summary>The value this phase has written so far (or the committed value if untouched).</summary>
    public T Pending(int id) => _next[id];

    /// <summary>Writes the next-state value. Visible to later phases after commit.</summary>
    public void Set(int id, T value)
    {
        _next[id] = value;
        _dirty = true;
    }

    /// <summary>Sets both buffers. Only for loading a scenario, never from a phase.</summary>
    public void Init(int id, T value)
    {
        _cur[id] = value;
        _next[id] = value;
    }

    public ReadOnlySpan<T> Committed => _cur;

    public void Commit()
    {
        if (!_dirty) return;
        Array.Copy(_next, _cur, _cur.Length);
        _dirty = false;
    }

    public void HashInto(StateHasher h) => h.Section(Name).AddSpan<T>(_cur);
}

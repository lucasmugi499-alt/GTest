using Cascade.Sim.Core;

namespace Cascade.Sim.Scheduling;

/// <summary>
/// A request from the player or the AI. Orders are the only way anything outside the sim changes it:
/// they queue up and apply in phase 0, in the order they arrived.
/// </summary>
public abstract class Order : IStateHashable
{
    /// <summary>Arrival number, assigned by the queue. Orders apply in this order.</summary>
    public long Seq { get; internal set; } = -1;
    /// <summary>The nation giving the order.</summary>
    public int Issuer { get; }

    protected Order(int issuer) => Issuer = issuer;

    /// <summary>Stable name for logs and hashing.</summary>
    public abstract string Kind { get; }

    /// <summary>Carries out the order, or refuses it with a reason the player can read.</summary>
    public abstract OrderOutcome Apply(TickContext ctx);

    /// <summary>Feed every field that affects <see cref="Apply"/>.</summary>
    protected abstract void HashFields(StateHasher h);

    public void HashInto(StateHasher h)
    {
        h.Add(Kind).Add(Seq).Add(Issuer);
        HashFields(h);
    }
}

/// <summary>What happened to an order: accepted, or refused with a reason.</summary>
public readonly record struct OrderOutcome(bool Accepted, string? Reason)
{
    public static readonly OrderOutcome Ok = new(true, null);
    public static OrderOutcome Refused(string reason) => new(false, reason);
}

public sealed class OrderQueue : IStateHashable
{
    private readonly List<Order> _pending = [];
    private long _nextSeq;

    public int PendingCount => _pending.Count;

    public void Enqueue(Order order)
    {
        if (order.Seq >= 0) throw new InvalidOperationException("Order was already queued.");
        order.Seq = _nextSeq++;
        _pending.Add(order);
    }

    /// <summary>Removes and returns all pending orders in arrival order.</summary>
    internal List<Order> Drain()
    {
        var list = new List<Order>(_pending);
        _pending.Clear();
        return list;
    }

    public void HashInto(StateHasher h)
    {
        h.Section("orders").Add(_nextSeq).Add(_pending.Count);
        foreach (var o in _pending) o.HashInto(h);
    }
}

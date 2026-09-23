using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;

namespace Cascade.Sim.Core;

/// <summary>Anything whose full state goes into the desync / replay hash.</summary>
public interface IStateHashable
{
    void HashInto(StateHasher h);
}

/// <summary>
/// Accumulates a 64-bit XxHash3 of the full sim state. Callers must feed values in a fixed order
/// (entity ID order), and never feed anything whose order isn't deterministic (e.g. Dictionary iteration).
/// </summary>
public sealed class StateHasher
{
    private readonly XxHash3 _hash = new();

    public StateHasher Add(long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, v);
        _hash.Append(b);
        return this;
    }

    public StateHasher Add(ulong v) => Add(unchecked((long)v));
    public StateHasher Add(int v) => Add((long)v);
    public StateHasher Add(bool v) => Add(v ? 1L : 0L);
    public StateHasher Add(Fixed v) => Add(v.Raw);
    public StateHasher Add(Fine v) => Add(v.Raw);

    public StateHasher Add(string s)
    {
        Add(s.Length);
        _hash.Append(Encoding.UTF8.GetBytes(s));
        return this;
    }

    /// <summary>Hashes a column's raw bytes. Only for little-endian blittable data (all supported platforms).</summary>
    public StateHasher AddSpan<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        Add(values.Length);
        _hash.Append(MemoryMarshal.AsBytes(values));
        return this;
    }

    /// <summary>Marks a section boundary so that moving a value between sections changes the hash.</summary>
    public StateHasher Section(string name) => Add(name);

    public StateHasher Add(IStateHashable item)
    {
        item.HashInto(this);
        return this;
    }

    public ulong Value => _hash.GetCurrentHashAsUInt64();

    public static string Format(ulong hash) => hash.ToString("X16");
}

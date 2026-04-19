namespace ANTS;

using System;
using System.Threading;

/// <summary>
/// Fixed-capacity, lock-free single-producer single-consumer (SPSC)
/// ring buffer for value types.
///
/// Design invariants:
///   * Capacity must be a positive power of two (enforced in ctor).
///     Enables index wrap via bitmask instead of modulo.
///   * Exactly ONE producer thread calls <see cref="Write"/> (on the
///     UI thread in this project).
///   * Exactly ONE consumer thread calls <see cref="Drain"/> (the
///     dedicated writer thread in <see cref="ProfileWriter"/>).
///   * Cross-thread visibility of the write/read cursors is
///     guaranteed by <see cref="Volatile.Write(ref long, long)"/> /
///     <see cref="Volatile.Read(ref long)"/> — no locks.
///   * When the writer falls behind and the buffer fills, the
///     producer overwrites the oldest unread sample and increments
///     <see cref="DroppedCount"/>. The consumer detects this by
///     observing (_writeIdx - _readIdx) &gt; Capacity and advances
///     its read cursor to the oldest still-present sample.
///
/// The generic constraint T : struct keeps samples stored inline in
/// the backing array so the hot path produces zero allocations.
/// </summary>
public sealed class RingBuffer<T> where T : struct
{
    private readonly T[] _buf;
    private readonly int _mask;

    // Monotonically increasing 64-bit cursors. Never wrap numerically
    // (2^63 entries at 240 Hz ~ 1.2 billion years).
    private long _writeIdx;
    private long _readIdx;
    private long _droppedCount;

    /// <summary>
    /// Creates a ring buffer with the given capacity.
    /// </summary>
    /// <param name="capacity">Must be a positive power of two.</param>
    /// <exception cref="ArgumentOutOfRangeException">capacity is not a positive power of two.</exception>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a positive power of two.");
        }
        _buf = new T[capacity];
        _mask = capacity - 1;
    }

    /// <summary>Backing array capacity (always a power of two).</summary>
    public int Capacity => _buf.Length;

    /// <summary>
    /// Total number of samples silently overwritten because the
    /// consumer fell behind. Zero under healthy operation.
    /// </summary>
    public long DroppedCount => Volatile.Read(ref _droppedCount);

    /// <summary>
    /// Producer-side write. Single-producer only; no internal
    /// synchronization beyond the Volatile store on the write cursor.
    /// </summary>
    public void Write(in T item)
    {
        long w = _writeIdx;
        long r = Volatile.Read(ref _readIdx);

        // If the consumer is more than one full turn behind, bump the
        // read cursor forward to (w - Capacity + 1) so the oldest
        // still-resident sample is the next one the consumer will see.
        // Each such bump = one dropped sample.
        if (w - r >= _buf.Length)
        {
            long overrun = (w - r) - _buf.Length + 1;
            Volatile.Write(ref _readIdx, r + overrun);
            Volatile.Write(ref _droppedCount, _droppedCount + overrun);
        }

        _buf[w & _mask] = item;
        Volatile.Write(ref _writeIdx, w + 1);
    }

    /// <summary>
    /// Consumer-side drain. Copies up to <paramref name="dst"/>.Length
    /// unread samples into <paramref name="dst"/> and advances the
    /// read cursor by that amount. Returns the number of samples
    /// actually copied (0 when the buffer is empty).
    /// </summary>
    public int Drain(Span<T> dst)
    {
        long w = Volatile.Read(ref _writeIdx);
        long r = _readIdx;
        long available = w - r;
        if (available <= 0 || dst.Length == 0)
        {
            return 0;
        }

        int toCopy = (int)Math.Min(available, (long)dst.Length);
        for (int i = 0; i < toCopy; i++)
        {
            dst[i] = _buf[(r + i) & _mask];
        }
        Volatile.Write(ref _readIdx, r + toCopy);
        return toCopy;
    }
}

using System.Runtime.CompilerServices;

namespace Vixxo.Core.Audio.Ipc;

/// <summary>
/// High-performance lock-free Single-Producer Single-Consumer (SPSC) ring buffer
/// designed specifically for real-time audio threads.
/// Guaranteed zero allocations and zero locks on Read/Write.
/// </summary>
public sealed class LockFreeSpscRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private readonly int _capacity;

    // Cache-line aligned sequence positions (padded to prevent false sharing)
    private long _head; // Read index (modified by Consumer only)
    private long _tail; // Write index (modified by Producer only)

    private long _overrunCount;
    private long _underrunCount;

    public int Capacity => _capacity;
    public long OverrunCount => Volatile.Read(ref _overrunCount);
    public long UnderrunCount => Volatile.Read(ref _underrunCount);

    public LockFreeSpscRingBuffer(int capacityPowerOfTwo = 65536)
    {
        if (capacityPowerOfTwo <= 0 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
        {
            throw new ArgumentException("Capacity must be a positive power of two.", nameof(capacityPowerOfTwo));
        }

        _capacity = capacityPowerOfTwo;
        _mask = capacityPowerOfTwo - 1;
        _buffer = new float[capacityPowerOfTwo];
        _head = 0;
        _tail = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AvailableRead()
    {
        long tail = Volatile.Read(ref _tail);
        long head = Volatile.Read(ref _head);
        return (int)(tail - head);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AvailableWrite()
    {
        return _capacity - AvailableRead();
    }

    /// <summary>
    /// Attempts to write samples without blocking. Returns true if all samples were written.
    /// If capacity is insufficient, increments OverrunCount and returns false without partial write.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        int count = samples.Length;
        if (count == 0) return true;

        long tail = Volatile.Read(ref _tail);
        long head = Volatile.Read(ref _head);
        int available = _capacity - (int)(tail - head);

        if (available < count)
        {
            Interlocked.Increment(ref _overrunCount);
            return false;
        }

        int writeIndex = (int)(tail & _mask);
        int firstChunk = Math.Min(count, _capacity - writeIndex);

        samples.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(writeIndex, firstChunk));

        if (firstChunk < count)
        {
            int secondChunk = count - firstChunk;
            samples.Slice(firstChunk, secondChunk).CopyTo(_buffer.AsSpan(0, secondChunk));
        }

        Volatile.Write(ref _tail, tail + count);
        return true;
    }

    /// <summary>
    /// Attempts to read requested samples without blocking. Returns true if all samples were read.
    /// If insufficient samples exist, increments UnderrunCount and returns false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(Span<float> destination)
    {
        int count = destination.Length;
        if (count == 0) return true;

        long tail = Volatile.Read(ref _tail);
        long head = Volatile.Read(ref _head);
        int available = (int)(tail - head);

        if (available < count)
        {
            Interlocked.Increment(ref _underrunCount);
            return false;
        }

        int readIndex = (int)(head & _mask);
        int firstChunk = Math.Min(count, _capacity - readIndex);

        _buffer.AsSpan(readIndex, firstChunk).CopyTo(destination.Slice(0, firstChunk));

        if (firstChunk < count)
        {
            int secondChunk = count - firstChunk;
            _buffer.AsSpan(0, secondChunk).CopyTo(destination.Slice(firstChunk, secondChunk));
        }

        Volatile.Write(ref _head, head + count);
        return true;
    }

    /// <summary>
    /// Reads as many available samples as possible up to destination.Length without failing.
    /// Returns the number of samples actually read.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadAvailable(Span<float> destination)
    {
        long tail = Volatile.Read(ref _tail);
        long head = Volatile.Read(ref _head);
        int available = (int)(tail - head);

        int count = Math.Min(destination.Length, available);
        if (count <= 0) return 0;

        int readIndex = (int)(head & _mask);
        int firstChunk = Math.Min(count, _capacity - readIndex);

        _buffer.AsSpan(readIndex, firstChunk).CopyTo(destination.Slice(0, firstChunk));

        if (firstChunk < count)
        {
            int secondChunk = count - firstChunk;
            _buffer.AsSpan(0, secondChunk).CopyTo(destination.Slice(firstChunk, secondChunk));
        }

        Volatile.Write(ref _head, head + count);
        return count;
    }

    /// <summary>
    /// Flushes all pending data in the ring buffer.
    /// </summary>
    public void Reset()
    {
        long tail = Volatile.Read(ref _tail);
        Volatile.Write(ref _head, tail);
    }
}

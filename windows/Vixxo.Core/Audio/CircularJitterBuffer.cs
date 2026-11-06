namespace Vixxo.Core.Audio;

public enum LatencyMode
{
    Low,
    Balanced,
    Stable
}

public sealed class CircularJitterBuffer
{
    private readonly object _lock = new();
    private readonly byte[] _buffer;
    private readonly int _capacityBytes;
    private int _head; // Write index
    private int _tail; // Read index
    private int _count; // Bytes currently in buffer

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly int _bytesPerSecond;

    private int _targetPrebufferBytes;
    private int _maxCapacityBytes;
    private bool _isPrebuffering = true;

    // Statistics
    private long _totalEnqueuedBytes;
    private long _totalReadBytes;
    private long _underrunCount;
    private long _overrunDropCount;

    public CircularJitterBuffer(
        int sampleRate = 48000,
        int channels = 1,
        int bitDepth = 16,
        int capacityMs = 500,
        LatencyMode latencyMode = LatencyMode.Balanced)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bytesPerSample = (bitDepth / 8) * channels;
        _bytesPerSecond = _sampleRate * _bytesPerSample;

        _capacityBytes = (_bytesPerSecond * capacityMs) / 1000;
        _buffer = new byte[_capacityBytes];

        SetLatencyMode(latencyMode);
    }

    public void SetLatencyMode(LatencyMode mode)
    {
        lock (_lock)
        {
            int prebufferMs = mode switch
            {
                LatencyMode.Low => 40,
                LatencyMode.Balanced => 80,
                LatencyMode.Stable => 140,
                _ => 80
            };

            int maxBufferMs = mode switch
            {
                LatencyMode.Low => 100,
                LatencyMode.Balanced => 200,
                LatencyMode.Stable => 350,
                _ => 200
            };

            _targetPrebufferBytes = (_bytesPerSecond * prebufferMs) / 1000;
            _maxCapacityBytes = Math.Min(_capacityBytes, (_bytesPerSecond * maxBufferMs) / 1000);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
            _isPrebuffering = true;
        }
    }

    public void Enqueue(ReadOnlySpan<byte> pcmData)
    {
        if (pcmData.IsEmpty) return;

        lock (_lock)
        {
            ReadOnlySpan<byte> dataToEnqueue = pcmData;
            if (dataToEnqueue.Length > _maxCapacityBytes)
            {
                int excessInput = dataToEnqueue.Length - _maxCapacityBytes;
                _overrunDropCount += excessInput;
                dataToEnqueue = dataToEnqueue.Slice(excessInput);
            }

            int maxOldAllowed = _maxCapacityBytes - dataToEnqueue.Length;
            if (_count > maxOldAllowed)
            {
                int bytesToDrop = _count - maxOldAllowed;
                _tail = (_tail + bytesToDrop) % _capacityBytes;
                _count -= bytesToDrop;
                _overrunDropCount += bytesToDrop;
            }

            // Write new data into circular buffer
            int written = 0;
            while (written < dataToEnqueue.Length)
            {
                int chunk = Math.Min(dataToEnqueue.Length - written, _capacityBytes - _head);
                dataToEnqueue.Slice(written, chunk).CopyTo(_buffer.AsSpan(_head, chunk));
                _head = (_head + chunk) % _capacityBytes;
                written += chunk;
            }

            _count += dataToEnqueue.Length;
            _totalEnqueuedBytes += pcmData.Length;

            // Check if prebuffering threshold is satisfied
            if (_isPrebuffering && _count >= _targetPrebufferBytes)
            {
                _isPrebuffering = false;
            }
        }
    }

    public int Read(Span<byte> destination)
    {
        if (destination.IsEmpty) return 0;

        lock (_lock)
        {
            if (_isPrebuffering)
            {
                // During pre-buffering, produce silence
                destination.Clear();
                return destination.Length;
            }

            if (_count == 0)
            {
                // Underrun: no audio available, output silence
                destination.Clear();
                _underrunCount++;
                _isPrebuffering = true; // Enter pre-buffering to absorb jitter
                return destination.Length;
            }

            int bytesToRead = Math.Min(destination.Length, _count);
            int read = 0;

            while (read < bytesToRead)
            {
                int chunk = Math.Min(bytesToRead - read, _capacityBytes - _tail);
                _buffer.AsSpan(_tail, chunk).CopyTo(destination.Slice(read, chunk));
                _tail = (_tail + chunk) % _capacityBytes;
                read += chunk;
            }

            _count -= bytesToRead;
            _totalReadBytes += bytesToRead;

            // If destination requested more bytes than available, fill remainder with silence
            if (bytesToRead < destination.Length)
            {
                destination.Slice(bytesToRead).Clear();
            }

            return destination.Length;
        }
    }

    // Properties for telemetry & monitoring
    public float CurrentDepthMs
    {
        get
        {
            lock (_lock)
            {
                return (_count * 1000.0f) / _bytesPerSecond;
            }
        }
    }

    public bool IsBuffering
    {
        get
        {
            lock (_lock) return _isPrebuffering;
        }
    }

    public long UnderrunCount
    {
        get
        {
            lock (_lock) return _underrunCount;
        }
    }

    public long OverrunDropCount
    {
        get
        {
            lock (_lock) return _overrunDropCount;
        }
    }

    public long TotalEnqueuedBytes
    {
        get
        {
            lock (_lock) return _totalEnqueuedBytes;
        }
    }

    public long TotalReadBytes
    {
        get
        {
            lock (_lock) return _totalReadBytes;
        }
    }
}

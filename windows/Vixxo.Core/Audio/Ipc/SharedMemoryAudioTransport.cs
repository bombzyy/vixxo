using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vixxo.Core.Audio.Ipc;

/// <summary>
/// High-throughput, zero-copy shared memory audio transport between C# and Python worker.
/// Uses Windows Named Memory Mapped Files and kernel auto-reset event synchronization.
/// </summary>
public sealed class SharedMemoryAudioTransport : IDisposable
{
    private const uint ShmMagic = 0x56495858; // 'VIXX'
    private const uint ShmVersion = 1;
    private const int ShmSize = 262144; // 256 KB
    private const int InBufferOffset = 128;
    private const int OutBufferOffset = 65664;

    private readonly string _shmName;
    private readonly string _eventInName;
    private readonly string _eventOutName;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private EventWaitHandle? _eventIn;
    private EventWaitHandle? _eventOut;

    private uint _sequenceNumber;
    private bool _isDisposed;

    public bool IsConnected => _mmf != null && _accessor != null && _eventIn != null && _eventOut != null;
    public uint CurrentSequence => _sequenceNumber;

    public SharedMemoryAudioTransport(string prefix = "vixxo_voice")
    {
        _shmName = $@"Local\{prefix}_shm";
        _eventInName = $@"Local\{prefix}_event_in";
        _eventOutName = $@"Local\{prefix}_event_out";
        _sequenceNumber = 0;
    }

    public bool Initialize(bool createIfMissing = true)
    {
        try
        {
            // First try opening existing handles created by Python worker
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(_shmName, MemoryMappedFileRights.ReadWrite);
                _accessor = _mmf.CreateViewAccessor(0, ShmSize, MemoryMappedFileAccess.ReadWrite);
                _eventIn = EventWaitHandle.OpenExisting(_eventInName);
                _eventOut = EventWaitHandle.OpenExisting(_eventOutName);
                return true;
            }
            catch when (createIfMissing)
            {
                // Fall back to creating if not present
                _mmf = MemoryMappedFile.CreateOrOpen(_shmName, ShmSize, MemoryMappedFileAccess.ReadWrite);
                _accessor = _mmf.CreateViewAccessor(0, ShmSize, MemoryMappedFileAccess.ReadWrite);
                _eventIn = new EventWaitHandle(false, EventResetMode.AutoReset, _eventInName);
                _eventOut = new EventWaitHandle(false, EventResetMode.AutoReset, _eventOutName);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SharedMemoryAudioTransport] Init error: {ex.Message}");
            Cleanup();
            return false;
        }
    }

    /// <summary>
    /// Sends an audio chunk to the worker and waits for converted audio.
    /// Returns true if converted chunk was successfully received and verified within timeout.
    /// </summary>
    public bool TransactChunk(
        ReadOnlySpan<float> inputSamples,
        uint inSampleRate,
        Span<float> outputBuffer,
        out int outputSamplesRead,
        out float processingMs,
        out long roundtripUs,
        int timeoutMs = 250)
    {
        outputSamplesRead = 0;
        processingMs = 0f;
        roundtripUs = 0;

        if (_accessor == null || _eventIn == null || _eventOut == null || _isDisposed)
        {
            return false;
        }

        int inCount = inputSamples.Length;
        if (inCount <= 0 || inCount > 16384)
        {
            return false;
        }

        uint seq = unchecked(++_sequenceNumber);
        long startTimestamp = Stopwatch.GetTimestamp();

        // Calculate CRC32 of input samples
        ReadOnlySpan<byte> inBytes = MemoryMarshal.AsBytes(inputSamples);
        uint inCrc = ComputeCrc32(inBytes);

        // Write input audio samples to SHM
        for (int i = 0; i < inCount; i++)
        {
            _accessor.Write(InBufferOffset + (i * 4), inputSamples[i]);
        }

        // Write header
        _accessor.Write(0, ShmMagic);
        _accessor.Write(4, ShmVersion);
        _accessor.Write(8, seq); // in_seq
        _accessor.Write(12, (uint)inCount); // in_samples
        _accessor.Write(16, inSampleRate); // in_sr
        _accessor.Write(20, 0u); // out_seq (reset)
        _accessor.Write(24, 0u); // out_samples (reset)
        _accessor.Write(28, 0u); // out_sr
        _accessor.Write(32, startTimestamp); // timestamp_us
        _accessor.Write(40, 0.0f); // processing_ms
        _accessor.Write(44, 1u); // flags: active
        _accessor.Write(48, inCrc); // in_checksum
        _accessor.Write(52, 0u); // out_checksum

        // Signal Python worker that input chunk is ready
        _eventIn.Set();

        // Wait for worker to finish inference
        bool signaled = _eventOut.WaitOne(timeoutMs);
        if (!signaled)
        {
            Debug.WriteLine($"[SharedMemoryAudioTransport] Timeout waiting for worker on seq {seq} ({timeoutMs}ms)");
            return false;
        }

        long endTimestamp = Stopwatch.GetTimestamp();
        roundtripUs = (endTimestamp - startTimestamp) * 1_000_000 / Stopwatch.Frequency;

        // Read and validate header response
        uint magic = _accessor.ReadUInt32(0);
        if (magic != ShmMagic)
        {
            Debug.WriteLine($"[SharedMemoryAudioTransport] Corrupted magic header: 0x{magic:X8}");
            return false;
        }

        uint outSeq = _accessor.ReadUInt32(20);
        if (outSeq != seq)
        {
            Debug.WriteLine($"[SharedMemoryAudioTransport] Stale packet ignored! Expected seq {seq}, got {outSeq}");
            return false;
        }

        uint outSamplesCount = _accessor.ReadUInt32(24);
        processingMs = _accessor.ReadSingle(40);
        uint outChecksum = _accessor.ReadUInt32(52);

        if (outSamplesCount == 0 || outSamplesCount > outputBuffer.Length)
        {
            Debug.WriteLine($"[SharedMemoryAudioTransport] Invalid out samples count: {outSamplesCount}");
            return false;
        }

        // Read output samples from SHM
        for (int i = 0; i < outSamplesCount; i++)
        {
            outputBuffer[i] = _accessor.ReadSingle(OutBufferOffset + (i * 4));
        }

        // Validate output checksum
        ReadOnlySpan<byte> outBytes = MemoryMarshal.AsBytes(outputBuffer.Slice(0, (int)outSamplesCount));
        uint calcCrc = ComputeCrc32(outBytes);
        if (outChecksum != 0 && calcCrc != outChecksum)
        {
            Debug.WriteLine($"[SharedMemoryAudioTransport] Output CRC mismatch: expected {outChecksum}, got {calcCrc}");
            return false;
        }

        outputSamplesRead = (int)outSamplesCount;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1));
            }
        }
        return ~crc;
    }

    private void Cleanup()
    {
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
        _eventIn?.Dispose();
        _eventIn = null;
        _eventOut?.Dispose();
        _eventOut = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Cleanup();
    }
}

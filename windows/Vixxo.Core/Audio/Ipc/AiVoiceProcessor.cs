using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Vixxo.Core.Audio.Ipc;

public enum AiEngineState
{
    Disabled,       // DSP or Bypass mode active
    Initializing,   // Worker process launching / loading model
    Streaming,      // Converted AI audio flowing normally
    Underrun,       // AI output starved; seamlessly crossfaded to DSP
    Faulted         // Worker crashed or pipe disconnected
}

/// <summary>
/// Bridge between the real-time WASAPI audio thread and the background RVC AI inference worker.
/// Enforces complete lock-free decoupling: WASAPI Read() never waits on IPC, GPU, or disk.
/// Implements latency-aware crossfading, underrun fallback to DSP, and bounded ring buffers.
/// </summary>
public sealed class AiVoiceProcessor : IDisposable
{
    private const int RingBufferCapacity = 65536; // ~1.36s at 48kHz
    private const int DefaultCrossfadeSamples = 480; // 10ms at 48kHz

    private readonly LockFreeSpscRingBuffer _inputRingBuffer;
    private readonly LockFreeSpscRingBuffer _outputRingBuffer;

    private readonly SharedMemoryAudioTransport _shmTransport;
    private readonly NamedPipeVoiceClient _controlClient;
    private readonly VoiceChangerProcessor _dspFallback;

    // Resampling scratch buffers (reusable, zero allocation in audio thread)
    private readonly float[] _resampleScratchIn;
    private readonly float[] _resampleScratchOut;

    // Crossfading state
    private readonly float[] _crossfadeBuffer;
    private float _aiWeight = 0.0f; // 1.0 = full AI, 0.0 = full DSP fallback
    private readonly float _crossfadeStep;

    // Background worker task
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;

    private int _sampleRate = 48000;
    private int _targetAiSampleRate = 40000;
    private int _chunkSizeMs = 80;
    private int _chunkSamples16k;

    private volatile AiEngineState _state = AiEngineState.Disabled;
    private float _lastProcessingMs;
    private float _lastRoundtripMs;

    public AiEngineState State => _state;
    public NamedPipeVoiceClient ControlClient => _controlClient;
    public SharedMemoryAudioTransport Transport => _shmTransport;
    public VoiceChangerProcessor DspFallback => _dspFallback;

    public long OverrunCount => _inputRingBuffer.OverrunCount + _outputRingBuffer.OverrunCount;
    public long UnderrunCount => _outputRingBuffer.UnderrunCount;
    public float LastProcessingMs => _lastProcessingMs;
    public float LastRoundtripMs => _lastRoundtripMs;
    public int InputQueueDepth => _inputRingBuffer.AvailableRead();
    public int OutputQueueDepth => _outputRingBuffer.AvailableRead();

    public AiVoiceProcessor(VoiceChangerProcessor dspFallback, int sampleRate = 48000, string prefix = "vixxo_voice")
    {
        _sampleRate = sampleRate;
        _dspFallback = dspFallback ?? throw new ArgumentNullException(nameof(dspFallback));

        _inputRingBuffer = new LockFreeSpscRingBuffer(RingBufferCapacity);
        _outputRingBuffer = new LockFreeSpscRingBuffer(RingBufferCapacity);

        _shmTransport = new SharedMemoryAudioTransport(prefix);
        _controlClient = new NamedPipeVoiceClient(prefix);

        _chunkSamples16k = (int)(16000 * (_chunkSizeMs / 1000.0f)); // 1280 samples for 80ms
        _resampleScratchIn = new float[16384];
        _resampleScratchOut = new float[32768];

        _crossfadeBuffer = new float[DefaultCrossfadeSamples];
        _crossfadeStep = 1.0f / DefaultCrossfadeSamples;

        _controlClient.WorkerExited += () =>
        {
            Debug.WriteLine("[AiVoiceProcessor] Worker exited. Forcing fallback to DSP.");
            _state = AiEngineState.Faulted;
            _outputRingBuffer.Reset();
        };

        _controlClient.WorkerDisconnected += () =>
        {
            Debug.WriteLine("[AiVoiceProcessor] Worker disconnected. Transitioning to fallback.");
            _state = AiEngineState.Faulted;
            _outputRingBuffer.Reset();
        };
    }

    /// <summary>
    /// Starts the background streaming loop.
    /// </summary>
    public bool Start(int chunkMs = 80, int targetAiSampleRate = 40000)
    {
        _chunkSizeMs = chunkMs;
        _targetAiSampleRate = targetAiSampleRate;
        _chunkSamples16k = (int)(16000 * (_chunkSizeMs / 1000.0f));

        if (!_shmTransport.Initialize(createIfMissing: true))
        {
            Debug.WriteLine("[AiVoiceProcessor] Failed to initialize Shared Memory Transport.");
            _state = AiEngineState.Faulted;
            return false;
        }

        _workerCts?.Cancel();
        _workerCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerStreamingLoop(_workerCts.Token));

        _state = AiEngineState.Streaming;
        return true;
    }

    public void Stop()
    {
        _state = AiEngineState.Disabled;
        _workerCts?.Cancel();
        try
        {
            _workerTask?.Wait(500);
        }
        catch { }

        _inputRingBuffer.Reset();
        _outputRingBuffer.Reset();
        _aiWeight = 0.0f;
    }

    /// <summary>
    /// Real-time audio processing seam called directly on WASAPI Read() audio thread.
    /// GUARANTEED ZERO LOCKS, ZERO IO, ZERO GPU WAITS, ZERO ALLOCATIONS.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ProcessMono(Span<float> samples)
    {
        int count = samples.Length;
        if (count == 0) return;

        // In disabled or faulted state, run pure DSP fallback with zero latency
        if (_state == AiEngineState.Disabled || _state == AiEngineState.Faulted)
        {
            _dspFallback.ProcessMono(samples);
            return;
        }

        // 1. Write dry input samples to input ring buffer for background AI worker
        // If buffer is full, TryWrite returns false and increments OverrunCount without blocking.
        _inputRingBuffer.TryWrite(samples);

        // 2. Compute DSP fallback audio in parallel on dry samples for glitch-free crossfading
        Span<float> dspAudio = stackalloc float[512];
        Span<float> aiAudio = stackalloc float[512];
        int offset = 0;

        while (offset < count)
        {
            int sliceCount = Math.Min(dspAudio.Length, count - offset);
            Span<float> inSlice = samples.Slice(offset, sliceCount);
            inSlice.CopyTo(dspAudio.Slice(0, sliceCount));
            _dspFallback.ProcessMono(dspAudio.Slice(0, sliceCount));

            // 3. Try to read converted AI audio from the output ring buffer
            int read = _outputRingBuffer.ReadAvailable(aiAudio.Slice(0, sliceCount));

            if (read == sliceCount)
            {
                // Full AI audio available: crossfade towards AI weight = 1.0
                _state = AiEngineState.Streaming;
                for (int i = 0; i < sliceCount; i++)
                {
                    _aiWeight = Math.Min(1.0f, _aiWeight + _crossfadeStep);
                    inSlice[i] = (aiAudio[i] * _aiWeight) + (dspAudio[i] * (1.0f - _aiWeight));
                }
            }
            else
            {
                // AI underrun! Smoothly crossfade towards DSP weight = 1.0 (aiWeight = 0.0)
                _state = AiEngineState.Underrun;
                for (int i = 0; i < sliceCount; i++)
                {
                    _aiWeight = Math.Max(0.0f, _aiWeight - _crossfadeStep);
                    float aiSample = (i < read) ? aiAudio[i] : dspAudio[i];
                    inSlice[i] = (aiSample * _aiWeight) + (dspAudio[i] * (1.0f - _aiWeight));
                }
            }

            offset += sliceCount;
        }
    }

    /// <summary>
    /// Background streaming thread: drains input ring buffer in 80ms chunks,
    /// sends across Shared Memory transport, receives converted AI audio, and pushes to output ring buffer.
    /// </summary>
    private void WorkerStreamingLoop(CancellationToken cancellationToken)
    {
        float[] inChunk16k = new float[_chunkSamples16k];
        float[] outChunkTgt = new float[16384];
        float[] outChunk48k = new float[16384];

        // Decimation ratio from 48kHz to 16kHz is exactly 3:1
        int inputSamplesNeeded48k = _chunkSamples16k * 3;
        float[] inBuffer48k = new float[inputSamplesNeeded48k];

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_inputRingBuffer.AvailableRead() < inputSamplesNeeded48k)
            {
                Thread.Sleep(5);
                continue;
            }

            // Read 80ms of 48kHz audio from input ring buffer
            int read = _inputRingBuffer.ReadAvailable(inBuffer48k);
            if (read < inputSamplesNeeded48k)
            {
                continue;
            }

            // Downsample 48kHz -> 16kHz (3:1 simple box/polyphase filter)
            for (int i = 0; i < _chunkSamples16k; i++)
            {
                int srcIdx = i * 3;
                inChunk16k[i] = (inBuffer48k[srcIdx] + inBuffer48k[srcIdx + 1] + inBuffer48k[srcIdx + 2]) / 3.0f;
            }

            // Transact across shared memory
            bool success = _shmTransport.TransactChunk(
                inChunk16k,
                inSampleRate: 16000,
                outputBuffer: outChunkTgt,
                out int outputSamplesRead,
                out float procMs,
                out long roundtripUs,
                timeoutMs: 200
            );

            if (success && outputSamplesRead > 0)
            {
                _lastProcessingMs = procMs;
                _lastRoundtripMs = roundtripUs / 1000.0f;

                // Resample AI output to 48kHz if target SR is 40kHz
                int samplesForOutput;
                if (_targetAiSampleRate == 48000)
                {
                    outChunkTgt.AsSpan(0, outputSamplesRead).CopyTo(outChunk48k);
                    samplesForOutput = outputSamplesRead;
                }
                else
                {
                    // Linear interpolation from 40kHz -> 48kHz (ratio 5:6)
                    double ratio = (double)_targetAiSampleRate / 48000.0;
                    samplesForOutput = (int)(outputSamplesRead / ratio);
                    if (samplesForOutput > outChunk48k.Length) samplesForOutput = outChunk48k.Length;

                    for (int i = 0; i < samplesForOutput; i++)
                    {
                        double srcPos = i * ratio;
                        int idx0 = (int)srcPos;
                        int idx1 = Math.Min(idx0 + 1, outputSamplesRead - 1);
                        float frac = (float)(srcPos - idx0);
                        outChunk48k[i] = outChunkTgt[idx0] + frac * (outChunkTgt[idx1] - outChunkTgt[idx0]);
                    }
                }

                // Push converted 48kHz audio into output ring buffer
                _outputRingBuffer.TryWrite(outChunk48k.AsSpan(0, samplesForOutput));
            }
            else
            {
                // Timeout or transaction failure
                _state = AiEngineState.Underrun;
            }
        }
    }

    public void Flush()
    {
        _inputRingBuffer.Reset();
        _outputRingBuffer.Reset();
        _aiWeight = 0.0f;
    }

    public void Dispose()
    {
        Stop();
        _shmTransport.Dispose();
        _controlClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

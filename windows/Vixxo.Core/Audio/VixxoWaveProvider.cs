using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Vixxo.Core.Audio;

public sealed class VixxoWaveProvider : IWaveProvider
{
    private readonly CircularJitterBuffer _jitterBuffer;
    private readonly WaveFormat _outputFormat;
    private readonly byte[] _monoScratchBuffer;
    private readonly float[] _floatScratchBuffer;
    private readonly bool _isIeeeFloat;

    private float _gainLinear = 1.0f;
    private bool _isMuted;
    private bool _isLimiterEnabled = true;
    private float _peakLevel;
    private float _rmsLevel;

    private DriftCompensator? _driftCompensator;
    private int _driftAdjustmentCounter;

    public AudioDspProcessor DspProcessor { get; }
    public VoiceChangerProcessor VoiceChanger { get; }
    public bool IsMonitorOutput { get; set; } = false;
    public SoundboardMixer? SoundboardMixer { get; set; }

    public VixxoWaveProvider(CircularJitterBuffer jitterBuffer, WaveFormat? endpointFormat = null)
    {
        _jitterBuffer = jitterBuffer ?? throw new ArgumentNullException(nameof(jitterBuffer));

        // Default or endpoint format: typically 48 kHz stereo 32-bit float or 16-bit PCM
        _outputFormat = endpointFormat ?? new WaveFormat(48000, 16, 2);

        _isIeeeFloat = _outputFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                       _outputFormat.BitsPerSample == 32 ||
                       (_outputFormat is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));

        _monoScratchBuffer = new byte[65536];
        _floatScratchBuffer = new float[32768];

        DspProcessor = new AudioDspProcessor(sampleRate: _outputFormat.SampleRate, cutoffHz: 80.0f)
        {
            IsHighPassFilterEnabled = false,
            IsNoiseGateEnabled = false
        };

        VoiceChanger = new VoiceChangerProcessor(sampleRate: _outputFormat.SampleRate);
    }

    public WaveFormat WaveFormat => _outputFormat;

    public void SetDriftCompensator(DriftCompensator? driftCompensator)
    {
        _driftCompensator = driftCompensator;
    }

    public float GainDb
    {
        get => (float)(20.0 * Math.Log10(_gainLinear <= 0.0001f ? 0.0001f : _gainLinear));
        set => _gainLinear = (float)Math.Pow(10.0, value / 20.0);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => _isMuted = value;
    }

    public bool IsLimiterEnabled
    {
        get => _isLimiterEnabled;
        set => _isLimiterEnabled = value;
    }

    public float PeakLevel => _peakLevel;
    public float RmsLevel => _rmsLevel;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0) return 0;

        int channels = Math.Max(1, _outputFormat.Channels);
        int bytesPerSample = _isIeeeFloat ? 4 : 2;
        int bytesPerFrame = channels * bytesPerSample;
        int framesRequested = count / bytesPerFrame;

        if (framesRequested > _floatScratchBuffer.Length)
        {
            framesRequested = _floatScratchBuffer.Length;
        }

        int monoBytesNeeded = framesRequested * 2; // 2 bytes per 16-bit mono sample
        if (monoBytesNeeded > _monoScratchBuffer.Length)
        {
            monoBytesNeeded = _monoScratchBuffer.Length;
            framesRequested = monoBytesNeeded / 2;
        }

        // Read mono 16-bit data from jitter buffer
        _jitterBuffer.Read(_monoScratchBuffer.AsSpan(0, monoBytesNeeded));

        // Convert mono 16-bit PCM to normalized float [-1.0, 1.0]
        for (int i = 0; i < framesRequested; i++)
        {
            short monoSample = (short)(_monoScratchBuffer[i * 2] | (_monoScratchBuffer[i * 2 + 1] << 8));
            _floatScratchBuffer[i] = monoSample / 32768.0f;
        }

        // Update drift compensator and perform micro-resampling adjustment if necessary
        if (_driftCompensator != null)
        {
            _driftCompensator.Update();
            float speedRatio = _driftCompensator.CurrentSpeedRatio;

            if (speedRatio > 1.001f)
            {
                // Buffer is growing too deep: gently drop 1 sample every 500 samples
                _driftAdjustmentCounter++;
                if (_driftAdjustmentCounter >= 500 && framesRequested > 2)
                {
                    _driftAdjustmentCounter = 0;
                    for (int i = framesRequested / 2; i < framesRequested - 1; i++)
                    {
                        _floatScratchBuffer[i] = _floatScratchBuffer[i + 1];
                    }
                }
            }
            else if (speedRatio < 0.999f)
            {
                // Buffer is draining: gently duplicate 1 sample every 500 samples
                _driftAdjustmentCounter++;
                if (_driftAdjustmentCounter >= 500 && framesRequested > 2)
                {
                    _driftAdjustmentCounter = 0;
                    int mid = framesRequested / 2;
                    _floatScratchBuffer[mid] = (_floatScratchBuffer[mid - 1] + _floatScratchBuffer[mid]) * 0.5f;
                }
            }
            else
            {
                _driftAdjustmentCounter = 0;
            }
        }

        // Apply DSP (HPF 80 Hz + Noise Gate)
        DspProcessor.ProcessMono(_floatScratchBuffer.AsSpan(0, framesRequested));

        // Apply Real-Time Voice Changer (Girl, Lady, Demon, Robot, Radio, etc.)
        VoiceChanger.ProcessMono(_floatScratchBuffer.AsSpan(0, framesRequested));

        // Mix soundboard clips before Limiter & Output gain
        if (IsMonitorOutput)
        {
            SoundboardMixer?.MixIntoMonitor(_floatScratchBuffer, framesRequested);
        }
        else
        {
            SoundboardMixer?.MixInto(_floatScratchBuffer, framesRequested);
        }

        // Apply Gain, Mute, Soft Limiter, and calculate Peak/RMS
        float sumSquares = 0f;
        float maxAbs = 0f;

        for (int i = 0; i < framesRequested; i++)
        {
            float s = _floatScratchBuffer[i];

            if (_isMuted)
            {
                s = 0f;
            }
            else
            {
                if (Math.Abs(_gainLinear - 1.0f) > 0.001f)
                {
                    s *= _gainLinear;
                }

                if (_isLimiterEnabled)
                {
                    if (s > 0.8f)
                        s = 0.8f + 0.2f * (float)Math.Tanh((s - 0.8f) / 0.2f);
                    else if (s < -0.8f)
                        s = -0.8f + 0.2f * (float)Math.Tanh((s + 0.8f) / 0.2f);
                }
                else
                {
                    s = Math.Clamp(s, -1.0f, 1.0f);
                }
            }

            float abs = Math.Abs(s);
            if (abs > maxAbs) maxAbs = abs;
            sumSquares += s * s;

            _floatScratchBuffer[i] = s;
        }

        _peakLevel = maxAbs;
        _rmsLevel = (float)Math.Sqrt(sumSquares / (framesRequested > 0 ? framesRequested : 1));

        // Render out to destination buffer
        if (_isIeeeFloat)
        {
            for (int i = 0; i < framesRequested; i++)
            {
                float sampleFloat = _floatScratchBuffer[i];
                int destIndex = offset + (i * bytesPerFrame);
                for (int ch = 0; ch < channels; ch++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(destIndex + (ch * 4), 4), sampleFloat);
                }
            }
        }
        else
        {
            for (int i = 0; i < framesRequested; i++)
            {
                float s = _floatScratchBuffer[i];
                short monoSample = (short)Math.Clamp(Math.Round(s * 32768.0), -32768.0, 32767.0);

                byte low = (byte)(monoSample & 0xFF);
                byte high = (byte)((monoSample >> 8) & 0xFF);

                int destIndex = offset + (i * bytesPerFrame);
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[destIndex + (ch * 2)] = low;
                    buffer[destIndex + (ch * 2) + 1] = high;
                }
            }
        }

        return framesRequested * bytesPerFrame;
    }
}

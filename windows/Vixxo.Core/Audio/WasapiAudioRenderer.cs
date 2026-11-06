using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Vixxo.Core.Audio;

public sealed class WasapiAudioRenderer : IDisposable
{
    private readonly CircularJitterBuffer _jitterBuffer;
    private WasapiOut? _wasapiOut;
    private VixxoWaveProvider? _waveProvider;
    private MMDevice? _currentDevice;
    private bool _isDisposed;
    private readonly object _lock = new();

    private float _gainDb = 0f;
    private bool _isMuted = false;
    private bool _isLimiterEnabled = true;
    private bool _isHighPassFilterEnabled = true;
    private bool _isNoiseGateEnabled = true;
    private float _noiseGateThresholdDb = -42.0f;
    private DriftCompensator? _driftCompensator;

    public WasapiAudioRenderer(CircularJitterBuffer jitterBuffer)
    {
        _jitterBuffer = jitterBuffer ?? throw new ArgumentNullException(nameof(jitterBuffer));
    }

    public bool IsActive => _wasapiOut?.PlaybackState == PlaybackState.Playing;
    public string? CurrentDeviceName => _currentDevice?.FriendlyName;
    public string? CurrentDeviceId => _currentDevice?.ID;

    public float GainDb
    {
        get => _gainDb;
        set
        {
            _gainDb = value;
            if (_waveProvider != null) _waveProvider.GainDb = value;
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_waveProvider != null) _waveProvider.IsMuted = value;
        }
    }

    public bool IsLimiterEnabled
    {
        get => _isLimiterEnabled;
        set
        {
            _isLimiterEnabled = value;
            if (_waveProvider != null) _waveProvider.IsLimiterEnabled = value;
        }
    }

    public bool IsHighPassFilterEnabled
    {
        get => _isHighPassFilterEnabled;
        set
        {
            _isHighPassFilterEnabled = value;
            if (_waveProvider != null) _waveProvider.DspProcessor.IsHighPassFilterEnabled = value;
        }
    }

    public bool IsNoiseGateEnabled
    {
        get => _isNoiseGateEnabled;
        set
        {
            _isNoiseGateEnabled = value;
            if (_waveProvider != null) _waveProvider.DspProcessor.IsNoiseGateEnabled = value;
        }
    }

    public float NoiseGateThresholdDb
    {
        get => _noiseGateThresholdDb;
        set
        {
            _noiseGateThresholdDb = value;
            if (_waveProvider != null) _waveProvider.DspProcessor.GateThresholdDb = value;
        }
    }

    public float PeakLevel => _waveProvider?.PeakLevel ?? 0f;
    public float RmsLevel => _waveProvider?.RmsLevel ?? 0f;
    public float CurrentBufferDepthMs => _jitterBuffer.CurrentDepthMs;
    public long UnderrunCount => _jitterBuffer.UnderrunCount;
    public long OverrunDropCount => _jitterBuffer.OverrunDropCount;
    public float CurrentSpeedRatio => _driftCompensator?.CurrentSpeedRatio ?? 1.0f;

    public void SetDriftCompensator(DriftCompensator? driftCompensator)
    {
        _driftCompensator = driftCompensator;
        _waveProvider?.SetDriftCompensator(driftCompensator);
    }

    public void Start(MMDevice device, int latencyMs = 50)
    {
        lock (_lock)
        {
            Stop();

            _currentDevice = device ?? throw new ArgumentNullException(nameof(device));

            // Obtain the endpoint mix format
            var mixFormat = _currentDevice.AudioClient.MixFormat;

            _waveProvider = new VixxoWaveProvider(_jitterBuffer, mixFormat)
            {
                GainDb = _gainDb,
                IsMuted = _isMuted,
                IsLimiterEnabled = _isLimiterEnabled
            };
            _waveProvider.DspProcessor.IsHighPassFilterEnabled = _isHighPassFilterEnabled;
            _waveProvider.DspProcessor.IsNoiseGateEnabled = _isNoiseGateEnabled;
            _waveProvider.DspProcessor.GateThresholdDb = _noiseGateThresholdDb;
            _waveProvider.SetDriftCompensator(_driftCompensator);

            try
            {
                // Try event-driven shared mode
                _wasapiOut = new WasapiOut(_currentDevice, AudioClientShareMode.Shared, useEventSync: true, latencyMs);
                _wasapiOut.Init(_waveProvider);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI Event-driven init failed ({ex.Message}), falling back to non-event sync...");
                _wasapiOut?.Dispose();
                _wasapiOut = new WasapiOut(_currentDevice, AudioClientShareMode.Shared, useEventSync: false, latencyMs);
                _wasapiOut.Init(_waveProvider);
            }

            _wasapiOut.Play();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_wasapiOut != null)
            {
                try
                {
                    _wasapiOut.Stop();
                }
                catch { }

                _wasapiOut.Dispose();
                _wasapiOut = null;
            }

            _waveProvider = null;
            _currentDevice = null;
            _jitterBuffer.Reset();
        }
    }

    public async Task PlaySelfTestToneAsync(int durationMs = 2000, float frequency = 440.0f, float amplitude = 0.25f, CancellationToken ct = default)
    {
        int sampleRate = 48000;
        int samplesPerFrame = sampleRate * 20 / 1000; // 960 samples per 20ms frame
        byte[] frameBytes = new byte[samplesPerFrame * 2];
        int totalFrames = durationMs / 20;

        double phase = 0;
        double phaseIncrement = 2.0 * Math.PI * frequency / sampleRate;

        for (int f = 0; f < totalFrames && !ct.IsCancellationRequested; f++)
        {
            for (int i = 0; i < samplesPerFrame; i++)
            {
                short sample = (short)(Math.Sin(phase) * amplitude * 32767.0);
                phase += phaseIncrement;
                if (phase >= 2.0 * Math.PI) phase -= 2.0 * Math.PI;

                frameBytes[i * 2] = (byte)(sample & 0xFF);
                frameBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            _jitterBuffer.Enqueue(frameBytes);
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}

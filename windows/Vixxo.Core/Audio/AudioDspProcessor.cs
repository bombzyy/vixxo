namespace Vixxo.Core.Audio;

public sealed class AudioDspProcessor
{
    private readonly int _sampleRate;

    // High-Pass Filter (80 Hz 2nd-order Butterworth)
    public bool IsHighPassFilterEnabled { get; set; } = true;
    private float _b0, _b1, _b2, _a1, _a2;
    private float _hpfX1, _hpfX2, _hpfY1, _hpfY2;

    // Noise Gate
    public bool IsNoiseGateEnabled { get; set; } = true;
    public float GateThreshold { get; set; } = 0.008f; // ~ -42 dBFS

    public float GateThresholdDb
    {
        get => (float)(20.0 * Math.Log10(GateThreshold <= 0.0001f ? 0.0001f : GateThreshold));
        set => GateThreshold = (float)Math.Pow(10.0, value / 20.0);
    }

    private float _gateGain = 1.0f; // Start open to prevent clipping first syllable
    private int _gateHoldCounter = 0;
    private readonly int _gateHoldSamples;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;

    public AudioDspProcessor(int sampleRate = 48000, float cutoffHz = 80.0f)
    {
        _sampleRate = sampleRate;

        // Calculate Biquad HPF coefficients
        double omega = 2.0 * Math.PI * cutoffHz / _sampleRate;
        double sinOmega = Math.Sin(omega);
        double cosOmega = Math.Cos(omega);
        double alpha = sinOmega / (2.0 * 0.70710678); // Q = 0.7071

        double a0 = 1.0 + alpha;
        _b0 = (float)((1.0 + cosOmega) / (2.0 * a0));
        _b1 = (float)(-(1.0 + cosOmega) / a0);
        _b2 = (float)((1.0 + cosOmega) / (2.0 * a0));
        _a1 = (float)(-2.0 * cosOmega / a0);
        _a2 = (float)((1.0 - alpha) / a0);

        // Noise Gate timing
        // 5ms attack, 60ms hold, 100ms release
        _attackCoeff = 1.0f - (float)Math.Exp(-1.0 / (0.005 * _sampleRate));
        _releaseCoeff = 1.0f - (float)Math.Exp(-1.0 / (0.100 * _sampleRate));
        _gateHoldSamples = (int)(0.060 * _sampleRate);
    }

    public void ProcessMono(Span<float> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float x = samples[i];

            // 1. High-Pass Filter (Rumble rejection)
            if (IsHighPassFilterEnabled)
            {
                float y = _b0 * x + _b1 * _hpfX1 + _b2 * _hpfX2 - _a1 * _hpfY1 - _a2 * _hpfY2;
                _hpfX2 = _hpfX1;
                _hpfX1 = x;
                _hpfY2 = _hpfY1;
                _hpfY1 = y;
                x = y;
            }

            // 2. Noise Gate (Room hiss / background noise rejection)
            if (IsNoiseGateEnabled)
            {
                float abs = Math.Abs(x);
                if (abs >= GateThreshold)
                {
                    _gateHoldCounter = _gateHoldSamples;
                    _gateGain += (_attackCoeff * (1.0f - _gateGain));
                }
                else if (_gateHoldCounter > 0)
                {
                    _gateHoldCounter--;
                }
                else
                {
                    _gateGain += (_releaseCoeff * (0.0f - _gateGain));
                }

                x *= _gateGain;
            }

            samples[i] = x;
        }
    }

    public void Reset()
    {
        _hpfX1 = 0; _hpfX2 = 0; _hpfY1 = 0; _hpfY2 = 0;
        _gateGain = 0;
        _gateHoldCounter = 0;
    }
}

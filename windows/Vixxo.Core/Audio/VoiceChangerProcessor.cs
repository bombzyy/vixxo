namespace Vixxo.Core.Audio;

public enum VoiceEngineMode
{
    AiNeural,       // RVC v2 Neural Voice Conversion (RTX 4050 GPU accelerated)
    FastDsp         // Low-latency time-domain DSP processing (< 5ms)
}

public enum VoicePreset
{
    Bypass,         // Clean normal microphone
    Girl,           // Young / Anime Girl (+5.8st, throat formant shift, breath sparkle)
    Lady,           // Adult Woman (+3.8st, natural female chest cut, vocal presence)
    Sage,           // Valorant Sage (+3.5st, tactical calm healer)
    Jett,           // Valorant Jett (+5.2st, crisp energetic duelist)
    Demon,          // Deep Chad / Demon (-4.8st, sub-bass rumble, menacing saturation)
    Helium,         // Helium / Chipmunk (+8.5st, extreme cartoon high pitch)
    Robot,          // Cyborg / Ring Modulator (55 Hz metallic carrier)
    Radio,          // Tactical Walkie-Talkie (350Hz-3400Hz AM radio bandpass + crunch)
    Echo            // Stadium / Cathedral Reverb
}

public sealed class VoiceChangerProcessor
{
    private readonly int _sampleRate;

    // Dual Delay-Line Rotating Head Pitch Shifter
    private readonly float[] _pitchDelayBuffer;
    private int _pitchWritePos;
    private double _phase;
    private readonly int _windowSize; // 1024 samples (~21.3ms at 48kHz)

    // Formant & Equalization BiQuad Filter States
    private readonly BiQuadFilter _lowShelfFilter;
    private readonly BiQuadFilter _peakingFilter;
    private readonly BiQuadFilter _highShelfFilter;
    private readonly BiQuadFilter _bandpassHpFilter;
    private readonly BiQuadFilter _bandpassLpFilter;

    // Robot Ring Modulator
    private double _ringModPhase = 0;
    private double _ringModStep;

    // Reverb Delay Lines (Schroeder comb filters)
    private readonly float[] _reverbBuffer1;
    private readonly float[] _reverbBuffer2;
    private int _reverbPos1 = 0;
    private int _reverbPos2 = 0;

    // Configuration Properties
    public bool IsEnabled { get; set; } = false;
    public VoiceEngineMode EngineMode { get; set; } = VoiceEngineMode.AiNeural;
    public float FormantShift { get; set; } = 0.0f;
    public float IndexRate { get; set; } = 0.75f;
    public string? CustomModelPath { get; set; }
    private VoicePreset _activePreset = VoicePreset.Bypass;
    private float _customPitchSemitones = 0f;

    public VoicePreset ActivePreset
    {
        get => _activePreset;
        set
        {
            _activePreset = value;
            ApplyPresetSettings(value);
        }
    }

    public float PitchSemitones
    {
        get => _customPitchSemitones;
        set
        {
            _customPitchSemitones = Math.Clamp(value, -12f, 12f);
        }
    }

    public float MixPercent { get; set; } = 100f; // 0 (dry) to 100 (wet)

    public VoiceChangerProcessor(int sampleRate = 48000)
    {
        _sampleRate = sampleRate;
        _windowSize = 1024;
        _pitchDelayBuffer = new float[4096];

        _lowShelfFilter = new BiQuadFilter();
        _peakingFilter = new BiQuadFilter();
        _highShelfFilter = new BiQuadFilter();
        _bandpassHpFilter = new BiQuadFilter();
        _bandpassLpFilter = new BiQuadFilter();

        _ringModStep = 2.0 * Math.PI * 55.0 / _sampleRate; // 55 Hz robot carrier

        // Reverb comb buffers (~33ms and ~41ms at 48kHz)
        _reverbBuffer1 = new float[(int)(0.033 * _sampleRate)];
        _reverbBuffer2 = new float[(int)(0.041 * _sampleRate)];

        ApplyPresetSettings(VoicePreset.Bypass);
    }

    public void ApplyPresetSettings(VoicePreset preset)
    {
        switch (preset)
        {
            case VoicePreset.Lady:
                _customPitchSemitones = 3.8f;
                _lowShelfFilter.SetLowShelf(_sampleRate, 160f, -8.0f);     // Cut male chest rumble
                _peakingFilter.SetPeakingEq(_sampleRate, 2800f, 4.5f, 1.2f); // Female formant boost
                _highShelfFilter.SetHighShelf(_sampleRate, 7500f, 2.5f);   // Feminine breath presence
                break;

            case VoicePreset.Sage:
                _customPitchSemitones = 3.5f;
                _lowShelfFilter.SetLowShelf(_sampleRate, 170f, -9.0f);     // Balanced chest reduction
                _peakingFilter.SetPeakingEq(_sampleRate, 2600f, 4.0f, 1.1f); // Tactical calm healer clarity
                _highShelfFilter.SetHighShelf(_sampleRate, 7200f, 2.0f);
                break;

            case VoicePreset.Jett:
                _customPitchSemitones = 5.2f;
                _lowShelfFilter.SetLowShelf(_sampleRate, 210f, -12.0f);    // Crisp duelist tone
                _peakingFilter.SetPeakingEq(_sampleRate, 3200f, 5.5f, 1.3f); // Sharp high presence
                _highShelfFilter.SetHighShelf(_sampleRate, 8500f, 3.5f);
                break;

            case VoicePreset.Girl:
                _customPitchSemitones = 5.8f;
                _lowShelfFilter.SetLowShelf(_sampleRate, 220f, -14.0f);    // Deep cut of lower resonances
                _peakingFilter.SetPeakingEq(_sampleRate, 3400f, 6.0f, 1.4f); // Youthful female throat resonance
                _highShelfFilter.SetHighShelf(_sampleRate, 9000f, 4.0f);   // Anime brightness & airy lift
                break;

            case VoicePreset.Demon:
                _customPitchSemitones = -4.8f;
                _lowShelfFilter.SetLowShelf(_sampleRate, 120f, 6.0f);      // Heavy chest bass
                _peakingFilter.SetPeakingEq(_sampleRate, 1500f, -3.0f, 1.0f);
                _highShelfFilter.SetHighShelf(_sampleRate, 5000f, -4.0f);  // Darker tone
                break;

            case VoicePreset.Helium:
                _customPitchSemitones = 8.5f;
                _lowShelfFilter.SetLowShelf(_sampleRate, 300f, -12.0f);
                _peakingFilter.SetPeakingEq(_sampleRate, 4000f, 3.0f, 1.0f);
                _highShelfFilter.SetHighShelf(_sampleRate, 8000f, 5.0f);
                break;

            case VoicePreset.Radio:
                _customPitchSemitones = 0.5f;
                _bandpassHpFilter.SetHighPass(_sampleRate, 350f, 0.707f);
                _bandpassLpFilter.SetLowPass(_sampleRate, 3400f, 0.707f);
                break;

            case VoicePreset.Robot:
                _customPitchSemitones = 0f;
                break;

            case VoicePreset.Echo:
                _customPitchSemitones = 0f;
                break;

            case VoicePreset.Bypass:
            default:
                _customPitchSemitones = 0f;
                break;
        }
    }

    public void ProcessMono(Span<float> samples)
    {
        if (!IsEnabled || _activePreset == VoicePreset.Bypass && Math.Abs(_customPitchSemitones) < 0.05f)
        {
            return;
        }

        float wetRatio = Math.Clamp(MixPercent / 100f, 0f, 1f);
        float dryRatio = 1.0f - wetRatio;

        // Calculate pitch ratio R = 2^(semitones / 12)
        double ratio = Math.Pow(2.0, _customPitchSemitones / 12.0);
        double deltaPhase = (ratio - 1.0) / _windowSize;

        for (int i = 0; i < samples.Length; i++)
        {
            float input = samples[i];
            float processed = input;

            // 1. Pitch Shifting (Dual Rotating Delay Line)
            if (Math.Abs(_customPitchSemitones) >= 0.05f)
            {
                // Write input to circular buffer
                _pitchDelayBuffer[_pitchWritePos] = input;

                // Advance phase
                _phase += deltaPhase;
                if (_phase >= 1.0) _phase -= 1.0;
                else if (_phase < 0.0) _phase += 1.0;

                double phase1 = _phase;
                double phase2 = phase1 + 0.5;
                if (phase2 >= 1.0) phase2 -= 1.0;

                // Raised-cosine window crossfades
                float w1 = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * phase1));
                float w2 = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * phase2));

                // Read head 1
                double delay1 = phase1 * _windowSize;
                double rPos1 = _pitchWritePos - delay1;
                while (rPos1 < 0) rPos1 += _pitchDelayBuffer.Length;

                int idx1 = (int)rPos1;
                float frac1 = (float)(rPos1 - idx1);
                int idx1Next = (idx1 + 1) % _pitchDelayBuffer.Length;
                float s1 = _pitchDelayBuffer[idx1] * (1.0f - frac1) + _pitchDelayBuffer[idx1Next] * frac1;

                // Read head 2
                double delay2 = phase2 * _windowSize;
                double rPos2 = _pitchWritePos - delay2;
                while (rPos2 < 0) rPos2 += _pitchDelayBuffer.Length;

                int idx2 = (int)rPos2;
                float frac2 = (float)(rPos2 - idx2);
                int idx2Next = (idx2 + 1) % _pitchDelayBuffer.Length;
                float s2 = _pitchDelayBuffer[idx2] * (1.0f - frac2) + _pitchDelayBuffer[idx2Next] * frac2;

                processed = s1 * w1 + s2 * w2;

                _pitchWritePos = (_pitchWritePos + 1) % _pitchDelayBuffer.Length;
            }

            // 2. Preset-Specific Formant & Harmonic Processing
            switch (_activePreset)
            {
                case VoicePreset.Lady:
                case VoicePreset.Girl:
                case VoicePreset.Helium:
                    processed = _lowShelfFilter.Process(processed);
                    processed = _peakingFilter.Process(processed);
                    processed = _highShelfFilter.Process(processed);
                    break;

                case VoicePreset.Demon:
                    processed = _lowShelfFilter.Process(processed);
                    processed = _peakingFilter.Process(processed);
                    processed = _highShelfFilter.Process(processed);
                    // Add subtle harmonic saturation
                    processed = (float)Math.Tanh(1.3 * processed);
                    break;

                case VoicePreset.Robot:
                    // Ring modulation with 55Hz carrier wave
                    float carrier = (float)Math.Sin(_ringModPhase);
                    _ringModPhase += _ringModStep;
                    if (_ringModPhase >= 2.0 * Math.PI) _ringModPhase -= 2.0 * Math.PI;

                    processed = processed * 0.35f + (processed * carrier) * 0.65f;
                    break;

                case VoicePreset.Radio:
                    // Bandpass filter 350Hz - 3400Hz
                    processed = _bandpassHpFilter.Process(processed);
                    processed = _bandpassLpFilter.Process(processed);
                    // Subtle AM radio overdrive crunch
                    processed = Math.Clamp(processed * 1.8f, -0.6f, 0.6f) * 1.4f;
                    break;

                case VoicePreset.Echo:
                    // Dual comb reverb
                    float echo1 = _reverbBuffer1[_reverbPos1];
                    float echo2 = _reverbBuffer2[_reverbPos2];
                    _reverbBuffer1[_reverbPos1] = processed + echo1 * 0.45f;
                    _reverbBuffer2[_reverbPos2] = processed + echo2 * 0.38f;

                    _reverbPos1 = (_reverbPos1 + 1) % _reverbBuffer1.Length;
                    _reverbPos2 = (_reverbPos2 + 1) % _reverbBuffer2.Length;

                    processed = processed * 0.65f + (echo1 + echo2) * 0.25f;
                    break;
            }

            // Mix dry and wet
            samples[i] = input * dryRatio + processed * wetRatio;
        }
    }

    public void Reset()
    {
        Array.Clear(_pitchDelayBuffer, 0, _pitchDelayBuffer.Length);
        _pitchWritePos = 0;
        _phase = 0;
        _lowShelfFilter.Reset();
        _peakingFilter.Reset();
        _highShelfFilter.Reset();
        _bandpassHpFilter.Reset();
        _bandpassLpFilter.Reset();
    }

    /// <summary>
    /// Direct BiQuad IIR Filter implementation based on Robert Bristow-Johnson Audio EQ Cookbook
    /// </summary>
    private sealed class BiQuadFilter
    {
        private float _b0, _b1, _b2, _a1, _a2;
        private float _x1, _x2, _y1, _y2;

        public void SetLowShelf(int sampleRate, float cutoffHz, float gainDb)
        {
            double a = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
            double cosW0 = Math.Cos(w0);
            double sinW0 = Math.Sin(w0);
            double alpha = sinW0 / 2.0 * Math.Sqrt(2.0); // S = 1
            double temp = 2.0 * Math.Sqrt(a) * alpha;

            double b0 = a * ((a + 1.0) - (a - 1.0) * cosW0 + temp);
            double b1 = 2.0 * a * ((a - 1.0) - (a + 1.0) * cosW0);
            double b2 = a * ((a + 1.0) - (a - 1.0) * cosW0 - temp);
            double a0 = (a + 1.0) + (a - 1.0) * cosW0 + temp;
            double a1 = -2.0 * ((a - 1.0) + (a + 1.0) * cosW0);
            double a2 = (a + 1.0) + (a - 1.0) * cosW0 - temp;

            SetCoeffs(b0, b1, b2, a0, a1, a2);
        }

        public void SetHighShelf(int sampleRate, float cutoffHz, float gainDb)
        {
            double a = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
            double cosW0 = Math.Cos(w0);
            double sinW0 = Math.Sin(w0);
            double alpha = sinW0 / 2.0 * Math.Sqrt(2.0);
            double temp = 2.0 * Math.Sqrt(a) * alpha;

            double b0 = a * ((a + 1.0) + (a - 1.0) * cosW0 + temp);
            double b1 = -2.0 * a * ((a - 1.0) + (a + 1.0) * cosW0);
            double b2 = a * ((a + 1.0) + (a - 1.0) * cosW0 - temp);
            double a0 = (a + 1.0) - (a - 1.0) * cosW0 + temp;
            double a1 = 2.0 * ((a - 1.0) - (a + 1.0) * cosW0);
            double a2 = (a + 1.0) - (a - 1.0) * cosW0 - temp;

            SetCoeffs(b0, b1, b2, a0, a1, a2);
        }

        public void SetPeakingEq(int sampleRate, float centerHz, float gainDb, float q)
        {
            double a = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * centerHz / sampleRate;
            double cosW0 = Math.Cos(w0);
            double sinW0 = Math.Sin(w0);
            double alpha = sinW0 / (2.0 * q);

            double b0 = 1.0 + alpha * a;
            double b1 = -2.0 * cosW0;
            double b2 = 1.0 - alpha * a;
            double a0 = 1.0 + alpha / a;
            double a1 = -2.0 * cosW0;
            double a2 = 1.0 - alpha / a;

            SetCoeffs(b0, b1, b2, a0, a1, a2);
        }

        public void SetHighPass(int sampleRate, float cutoffHz, float q)
        {
            double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
            double cosW0 = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * q);

            double b0 = (1.0 + cosW0) / 2.0;
            double b1 = -(1.0 + cosW0);
            double b2 = (1.0 + cosW0) / 2.0;
            double a0 = 1.0 + alpha;
            double a1 = -2.0 * cosW0;
            double a2 = 1.0 - alpha;

            SetCoeffs(b0, b1, b2, a0, a1, a2);
        }

        public void SetLowPass(int sampleRate, float cutoffHz, float q)
        {
            double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
            double cosW0 = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * q);

            double b0 = (1.0 - cosW0) / 2.0;
            double b1 = 1.0 - cosW0;
            double b2 = (1.0 - cosW0) / 2.0;
            double a0 = 1.0 + alpha;
            double a1 = -2.0 * cosW0;
            double a2 = 1.0 - alpha;

            SetCoeffs(b0, b1, b2, a0, a1, a2);
        }

        private void SetCoeffs(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = (float)(b0 / a0);
            _b1 = (float)(b1 / a0);
            _b2 = (float)(b2 / a0);
            _a1 = (float)(a1 / a0);
            _a2 = (float)(a2 / a0);
        }

        public float Process(float inSample)
        {
            float outSample = _b0 * inSample + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = inSample;
            _y2 = _y1;
            _y1 = outSample;
            return outSample;
        }

        public void Reset()
        {
            _x1 = 0; _x2 = 0; _y1 = 0; _y2 = 0;
        }
    }
}

using Vixxo.Core.Audio;
using Xunit;

namespace Vixxo.Tests;

public class AudioDspProcessorTests
{
    [Fact]
    public void HighPassFilter_AttenuatesLowFrequencies()
    {
        var dsp = new AudioDspProcessor(sampleRate: 48000, cutoffHz: 80.0f)
        {
            IsHighPassFilterEnabled = true,
            IsNoiseGateEnabled = false
        };

        // 20 Hz sine wave (sub-bass / rumble)
        int sampleRate = 48000;
        float[] samples20Hz = new float[sampleRate]; // 1 second
        for (int i = 0; i < samples20Hz.Length; i++)
        {
            samples20Hz[i] = (float)Math.Sin(2.0 * Math.PI * 20.0 * i / sampleRate);
        }

        dsp.ProcessMono(samples20Hz);

        // Calculate peak amplitude of the last 480 samples (steady state)
        float max20Hz = 0f;
        for (int i = sampleRate - 480; i < sampleRate; i++)
        {
            max20Hz = Math.Max(max20Hz, Math.Abs(samples20Hz[i]));
        }

        // 20 Hz is two octaves below 80 Hz (-12dB/octave for 2nd order Butterworth -> ~ -24dB attenuation -> ~0.06 amplitude)
        Assert.True(max20Hz < 0.20f, $"Expected 20Hz signal to be attenuated below 0.20, but was {max20Hz}");
    }

    [Fact]
    public void HighPassFilter_PassesHighFrequencies()
    {
        var dsp = new AudioDspProcessor(sampleRate: 48000, cutoffHz: 80.0f)
        {
            IsHighPassFilterEnabled = true,
            IsNoiseGateEnabled = false
        };

        // 1000 Hz sine wave (voice range)
        int sampleRate = 48000;
        float[] samples1000Hz = new float[sampleRate];
        for (int i = 0; i < samples1000Hz.Length; i++)
        {
            samples1000Hz[i] = (float)Math.Sin(2.0 * Math.PI * 1000.0 * i / sampleRate);
        }

        dsp.ProcessMono(samples1000Hz);

        float max1000Hz = 0f;
        for (int i = sampleRate - 480; i < sampleRate; i++)
        {
            max1000Hz = Math.Max(max1000Hz, Math.Abs(samples1000Hz[i]));
        }

        // 1000 Hz should pass through nearly unattenuated (>= 0.95)
        Assert.True(max1000Hz >= 0.95f, $"Expected 1000Hz signal to pass with >= 0.95, but was {max1000Hz}");
    }

    [Fact]
    public void NoiseGate_AttenuatesQuietHiss()
    {
        var dsp = new AudioDspProcessor(sampleRate: 48000)
        {
            IsHighPassFilterEnabled = false,
            IsNoiseGateEnabled = true,
            GateThreshold = 0.01f // -40 dB
        };

        // Low level noise (0.002 amplitude)
        int sampleRate = 48000;
        float[] quietSamples = new float[sampleRate / 2]; // 0.5s
        for (int i = 0; i < quietSamples.Length; i++)
        {
            quietSamples[i] = 0.002f;
        }

        dsp.ProcessMono(quietSamples);

        // Tail of the quiet signal should be attenuated towards zero
        float lastSample = Math.Abs(quietSamples[^1]);
        Assert.True(lastSample < 0.0005f, $"Expected gate to close on quiet noise, but last sample was {lastSample}");
    }

    [Fact]
    public void NoiseGate_OpensOnNormalVoice()
    {
        var dsp = new AudioDspProcessor(sampleRate: 48000)
        {
            IsHighPassFilterEnabled = false,
            IsNoiseGateEnabled = true,
            GateThreshold = 0.01f // -40 dB
        };

        // Voice signal (0.3 amplitude, ~ -10 dB)
        int sampleRate = 48000;
        float[] voiceSamples = new float[sampleRate / 4]; // 250ms
        for (int i = 0; i < voiceSamples.Length; i++)
        {
            voiceSamples[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.3f;
        }

        dsp.ProcessMono(voiceSamples);

        float lastPeak = 0f;
        for (int i = voiceSamples.Length - 100; i < voiceSamples.Length; i++)
        {
            lastPeak = Math.Max(lastPeak, Math.Abs(voiceSamples[i]));
        }

        // Should open gate and pass voice at full amplitude
        Assert.True(lastPeak >= 0.28f, $"Expected gate to open for voice, but peak was {lastPeak}");
    }
}

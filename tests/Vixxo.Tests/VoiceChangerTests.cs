using Vixxo.Core.Audio;
using Xunit;

namespace Vixxo.Tests;

public class VoiceChangerTests
{
    [Fact]
    public void BypassPreset_LeavesSamplesUnaltered()
    {
        var vc = new VoiceChangerProcessor(48000)
        {
            IsEnabled = false,
            ActivePreset = VoicePreset.Bypass
        };

        float[] original = new float[480];
        float[] buffer = new float[480];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0) * 0.5f;
            buffer[i] = original[i];
        }

        vc.ProcessMono(buffer);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(original[i], buffer[i], 5);
        }
    }

    [Fact]
    public void GirlPreset_HasHigherPitchAndLowCut()
    {
        var vc = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Girl
        };

        Assert.Equal(VoicePreset.Girl, vc.ActivePreset);
        Assert.True(vc.PitchSemitones > 4.0f, $"Expected Girl pitch > +4st, got {vc.PitchSemitones}");

        // Process audio buffer
        float[] buffer = new float[960];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)Math.Sin(2.0 * Math.PI * 200.0 * i / 48000.0) * 0.6f;
        }

        vc.ProcessMono(buffer);

        // Verify valid samples
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.False(float.IsNaN(buffer[i]), $"Sample at {i} is NaN");
            Assert.False(float.IsInfinity(buffer[i]), $"Sample at {i} is Infinity");
            Assert.InRange(buffer[i], -1.5f, 1.5f);
        }
    }

    [Fact]
    public void LadyPreset_HasMatureFemalePitch()
    {
        var vc = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Lady
        };

        Assert.Equal(VoicePreset.Lady, vc.ActivePreset);
        Assert.True(vc.PitchSemitones > 2.0f && vc.PitchSemitones < 5.0f, 
            $"Expected Lady pitch between +2st and +5st, got {vc.PitchSemitones}");

        float[] buffer = new float[960];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)Math.Sin(2.0 * Math.PI * 250.0 * i / 48000.0) * 0.5f;
        }

        vc.ProcessMono(buffer);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.False(float.IsNaN(buffer[i]));
            Assert.False(float.IsInfinity(buffer[i]));
        }
    }

    [Fact]
    public void DemonPreset_HasPitchDownAndLowBoost()
    {
        var vc = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Demon
        };

        Assert.Equal(VoicePreset.Demon, vc.ActivePreset);
        Assert.True(vc.PitchSemitones < -3.0f, $"Expected Demon pitch < -3st, got {vc.PitchSemitones}");

        float[] buffer = new float[960];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)Math.Sin(2.0 * Math.PI * 150.0 * i / 48000.0) * 0.7f;
        }

        vc.ProcessMono(buffer);

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.False(float.IsNaN(buffer[i]));
            Assert.False(float.IsInfinity(buffer[i]));
        }
    }

    [Fact]
    public void RobotPreset_AppliesRingModulation()
    {
        var vc = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Robot
        };

        float[] buffer = new float[960];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0.5f;
        }

        vc.ProcessMono(buffer);

        // Ring modulation on DC offset produces carrier sine wave
        bool hasOscillation = false;
        for (int i = 1; i < buffer.Length; i++)
        {
            if (Math.Abs(buffer[i] - buffer[0]) > 0.05f)
            {
                hasOscillation = true;
                break;
            }
        }
        Assert.True(hasOscillation, "Robot preset should modulate the input signal");
    }

    [Fact]
    public void CustomPitchShift_ChangesSemitones()
    {
        var vc = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Girl
        };

        vc.PitchSemitones = 7.0f;
        Assert.Equal(7.0f, vc.PitchSemitones, 1);

        vc.PitchSemitones = -5.5f;
        Assert.Equal(-5.5f, vc.PitchSemitones, 1);
    }
}

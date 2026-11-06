using Vixxo.Core.Audio;
using Xunit;

namespace Vixxo.Tests;

public sealed class SoundboardTests
{
    [Fact]
    public void SoundSynthesizer_GeneratesValidWav_WithRiffHeader()
    {
        byte[] spikePlanted = SoundSynthesizer.GenerateSpikePlanted();
        Assert.NotNull(spikePlanted);
        Assert.True(spikePlanted.Length > 1000);

        // Check RIFF header
        Assert.Equal((byte)'R', spikePlanted[0]);
        Assert.Equal((byte)'I', spikePlanted[1]);
        Assert.Equal((byte)'F', spikePlanted[2]);
        Assert.Equal((byte)'F', spikePlanted[3]);

        // Check WAVE
        Assert.Equal((byte)'W', spikePlanted[8]);
        Assert.Equal((byte)'A', spikePlanted[9]);
        Assert.Equal((byte)'V', spikePlanted[10]);
        Assert.Equal((byte)'E', spikePlanted[11]);
    }

    [Fact]
    public void SoundSynthesizer_GeneratesAllValorantAndMemeSounds()
    {
        Assert.NotEmpty(SoundSynthesizer.GenerateSpikeDefusing());
        Assert.NotEmpty(SoundSynthesizer.GenerateSpikeDown());
        Assert.NotEmpty(SoundSynthesizer.GenerateLastPlayerStanding());
        Assert.NotEmpty(SoundSynthesizer.GenerateHeadshotDink());
        Assert.NotEmpty(SoundSynthesizer.GenerateAirhorn());
        Assert.NotEmpty(SoundSynthesizer.GenerateSadTrombone());
        Assert.NotEmpty(SoundSynthesizer.GenerateRecordScratch());
    }

    [Fact]
    public void SoundboardMixer_CanInitialize_AndMixAudioIntoMicBuffer()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vixxo_test_sounds_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mixer = new SoundboardMixer(tempDir);
            Assert.NotEmpty(mixer.Clips);
            Assert.Contains(mixer.Clips, c => c.Name == "Spike Planted");

            var spikeClip = mixer.Clips.First(c => c.Name == "Spike Planted");
            mixer.PlayClip(spikeClip.Id);

            Assert.True(mixer.IsPlaying);
            Assert.Equal("Spike Planted", mixer.CurrentPlayingClipName);

            // Mix into 100 samples
            float[] micSamples = new float[100];
            Array.Fill(micSamples, 0.1f);

            mixer.MixInto(micSamples, 100);

            // Audio samples should be modified by the soundboard mixer
            Assert.Contains(micSamples, s => Math.Abs(s - 0.1f) > 0.0001f);

            mixer.StopAll();
            Assert.False(mixer.IsPlaying);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SoundboardMixer_CanDeleteClip()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vixxo_test_sounds_del_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mixer = new SoundboardMixer(tempDir);
            int initialCount = mixer.Clips.Count;
            Assert.True(initialCount > 0);

            var firstClip = mixer.Clips.First();
            bool deleted = mixer.DeleteClip(firstClip.Id);

            Assert.True(deleted);
            Assert.Equal(initialCount - 1, mixer.Clips.Count);
            Assert.DoesNotContain(mixer.Clips, c => c.Id == firstClip.Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void SoundboardMixer_CanMixIntoMonitorHeadphones()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vixxo_test_sounds_mon_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mixer = new SoundboardMixer(tempDir)
            {
                MonitorClipsInHeadphones = true
            };

            var clip = mixer.Clips.First();
            mixer.PlayClip(clip.Id);

            float[] monitorSamples = new float[100];
            Array.Fill(monitorSamples, 0.0f);

            mixer.MixIntoMonitor(monitorSamples, 100);

            Assert.Contains(monitorSamples, s => Math.Abs(s) > 0.0001f);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}

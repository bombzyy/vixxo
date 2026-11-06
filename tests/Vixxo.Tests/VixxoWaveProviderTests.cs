using NAudio.Wave;
using Vixxo.Core.Audio;

namespace Vixxo.Tests;

public class VixxoWaveProviderTests
{
    [Fact]
    public void Mute_ForcesOutputToExactZeroes()
    {
        var jitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);
        // Prime buffer
        jitterBuffer.Enqueue(new byte[3840]);
        jitterBuffer.Read(new byte[3840]);

        // Enqueue loud audio samples
        byte[] loudPcm = new byte[1920];
        Array.Fill(loudPcm, (byte)0x7F);
        jitterBuffer.Enqueue(loudPcm);

        var provider = new VixxoWaveProvider(jitterBuffer, new WaveFormat(48000, 16, 2))
        {
            IsMuted = true
        };

        byte[] outputBuffer = new byte[3840]; // Stereo = 2x mono bytes
        int read = provider.Read(outputBuffer, 0, outputBuffer.Length);

        Assert.Equal(3840, read);
        Assert.All(outputBuffer, b => Assert.Equal(0, b));
        Assert.Equal(0f, provider.PeakLevel);
    }

    [Fact]
    public void StereoMapping_DuplicatesMonoChannelToBothLeftAndRight()
    {
        var jitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);
        // Prime buffer
        jitterBuffer.Enqueue(new byte[3840]);
        jitterBuffer.Read(new byte[3840]);

        // Create 1 mono 16-bit sample: 0x1234
        byte[] monoData = [0x34, 0x12];
        jitterBuffer.Enqueue(monoData);

        var provider = new VixxoWaveProvider(jitterBuffer, new WaveFormat(48000, 16, 2));

        byte[] output = new byte[4]; // 2 bytes Left + 2 bytes Right
        int read = provider.Read(output, 0, 4);

        Assert.Equal(4, read);
        // Left channel
        Assert.Equal(0x34, output[0]);
        Assert.Equal(0x12, output[1]);
        // Right channel (must match left)
        Assert.Equal(0x34, output[2]);
        Assert.Equal(0x12, output[3]);
    }

    [Fact]
    public void GainDb_ScalesAudioSamplesProperly()
    {
        var jitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);
        jitterBuffer.Enqueue(new byte[3840]);
        jitterBuffer.Read(new byte[3840]);

        // Mono sample = 1000
        short originalSample = 1000;
        byte[] monoData = BitConverter.GetBytes(originalSample);
        jitterBuffer.Enqueue(monoData);

        var provider = new VixxoWaveProvider(jitterBuffer, new WaveFormat(48000, 16, 2))
        {
            GainDb = 6.02f // Approx +6 dB = 2x amplitude
        };

        byte[] output = new byte[4];
        provider.Read(output, 0, 4);

        short leftSample = BitConverter.ToInt16(output, 0);
        // 1000 * 2 = ~2000
        Assert.InRange(leftSample, 1980, 2020);
    }
}

using Vixxo.Core.Audio;
using Xunit;

namespace Vixxo.Tests;

public class DriftCompensatorTests
{
    [Fact]
    public void DriftCompensator_NormalDepth_SpeedRatioIsOne()
    {
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Balanced);
        var compensator = new DriftCompensator(buffer, targetDepthMs: 80);

        // Pre-fill buffer with 80ms of audio (80 * 48000 / 1000 * 2 = 7680 bytes)
        byte[] pcm80ms = new byte[7680];
        buffer.Enqueue(pcm80ms);

        for (int i = 0; i < 50; i++)
        {
            compensator.Update();
        }

        Assert.Equal(1.000f, compensator.CurrentSpeedRatio);
    }

    [Fact]
    public void DriftCompensator_ExcessiveDepth_SpeedsUp()
    {
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Balanced);
        var compensator = new DriftCompensator(buffer, targetDepthMs: 80);

        // Pre-fill buffer with 160ms of audio (15360 bytes)
        byte[] pcm160ms = new byte[15360];
        buffer.Enqueue(pcm160ms);

        for (int i = 0; i < 50; i++)
        {
            compensator.Update();
        }

        Assert.Equal(1.002f, compensator.CurrentSpeedRatio);
    }

    [Fact]
    public void DriftCompensator_LowDepth_SlowsDown()
    {
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Balanced);
        var compensator = new DriftCompensator(buffer, targetDepthMs: 80);

        // Pre-fill buffer with 80ms so buffering state clears
        byte[] pcm80ms = new byte[7680];
        buffer.Enqueue(pcm80ms);
        Assert.False(buffer.IsBuffering);

        // Drain most of it
        byte[] readSink = new byte[5000];
        buffer.Read(readSink);

        // Depth is now around ~28ms (below 80 * 0.70 = 56ms)
        for (int i = 0; i < 50; i++)
        {
            compensator.Update();
        }

        Assert.Equal(0.998f, compensator.CurrentSpeedRatio);
    }
}

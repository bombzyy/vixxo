using Vixxo.Core.Audio;

namespace Vixxo.Tests;

public class CircularJitterBufferTests
{
    [Fact]
    public void Prebuffering_ProducesSilence_UntilThresholdReached()
    {
        // 48 kHz mono 16-bit = 96,000 bytes/sec
        // Low mode: prebuffer 40 ms = 3840 bytes
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);

        Assert.True(buffer.IsBuffering);

        // Enqueue only 1920 bytes (20 ms)
        byte[] audioData = new byte[1920];
        Array.Fill(audioData, (byte)0x7F);
        buffer.Enqueue(audioData);

        Assert.True(buffer.IsBuffering);

        // Read attempt should return silence
        byte[] readDest = new byte[1920];
        Array.Fill(readDest, (byte)0xFF);
        buffer.Read(readDest);

        Assert.All(readDest, b => Assert.Equal(0, b));

        // Enqueue remaining 1920 bytes to cross 3840 threshold
        buffer.Enqueue(audioData);
        Assert.False(buffer.IsBuffering);
    }

    [Fact]
    public void Buffer_MaintainsExactSampleSequence()
    {
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);

        // Push enough to cross prebuffer
        byte[] chunk1 = [1, 2, 3, 4];
        byte[] chunk2 = [5, 6, 7, 8];

        // Satisfy prebuffer with zeroes first
        byte[] primer = new byte[3840];
        buffer.Enqueue(primer);
        byte[] dump = new byte[3840];
        buffer.Read(dump);

        // Now enqueue distinct chunks
        buffer.Enqueue(chunk1);
        buffer.Enqueue(chunk2);

        byte[] output = new byte[8];
        buffer.Read(output);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], output);
    }

    [Fact]
    public void Underrun_OutputsSilence_AndIncrementsCounter()
    {
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);

        // Satisfy prebuffer
        buffer.Enqueue(new byte[3840]);
        byte[] dump = new byte[3840];
        buffer.Read(dump);

        long underrunsBefore = buffer.UnderrunCount;

        // Now buffer is empty; read should underrun and output silence
        byte[] dest = new byte[100];
        Array.Fill(dest, (byte)0xEE);
        buffer.Read(dest);

        Assert.All(dest, b => Assert.Equal(0, b));
        Assert.Equal(underrunsBefore + 1, buffer.UnderrunCount);
    }

    [Fact]
    public void Overrun_DropsExcessSamples_WithoutInfiniteGrowth()
    {
        // Low mode: max depth 100 ms = 9600 bytes
        var buffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 200, LatencyMode.Low);

        // Enqueue 20,000 bytes (far exceeding 9600 bytes max depth)
        byte[] hugeAudio = new byte[20000];
        Array.Fill(hugeAudio, (byte)0x42);
        buffer.Enqueue(hugeAudio);

        Assert.True(buffer.OverrunDropCount > 0);
        Assert.True(buffer.CurrentDepthMs <= 110f); // Bounded around max buffer
    }
}

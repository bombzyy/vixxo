using Vixxo.Core.Audio.Ipc;
using Xunit;

namespace Vixxo.Tests;

public class LockFreeSpscRingBufferTests
{
    [Fact]
    public void Buffer_InitialState_IsEmpty()
    {
        var buffer = new LockFreeSpscRingBuffer(1024);
        Assert.Equal(1024, buffer.Capacity);
        Assert.Equal(0, buffer.AvailableRead());
        Assert.Equal(1024, buffer.AvailableWrite());
        Assert.Equal(0, buffer.OverrunCount);
        Assert.Equal(0, buffer.UnderrunCount);
    }

    [Fact]
    public void Buffer_WriteAndRead_ExactMatch()
    {
        var buffer = new LockFreeSpscRingBuffer(1024);
        float[] input = new float[256];
        for (int i = 0; i < input.Length; i++) input[i] = i * 0.01f;

        bool written = buffer.TryWrite(input);
        Assert.True(written);
        Assert.Equal(256, buffer.AvailableRead());

        float[] output = new float[256];
        bool read = buffer.TryRead(output);
        Assert.True(read);
        Assert.Equal(0, buffer.AvailableRead());

        for (int i = 0; i < input.Length; i++)
        {
            Assert.Equal(input[i], output[i]);
        }
    }

    [Fact]
    public void Buffer_WrapAround_SucceedsWithoutDataCorruption()
    {
        var buffer = new LockFreeSpscRingBuffer(1024);
        float[] chunk = new float[600];
        float[] outChunk = new float[600];

        // First write/read (advances head/tail past 0)
        for (int i = 0; i < chunk.Length; i++) chunk[i] = 1.0f;
        Assert.True(buffer.TryWrite(chunk));
        Assert.True(buffer.TryRead(outChunk));

        // Second write (wraps around 1024 boundary)
        for (int i = 0; i < chunk.Length; i++) chunk[i] = i;
        Assert.True(buffer.TryWrite(chunk));
        Assert.True(buffer.TryRead(outChunk));

        for (int i = 0; i < chunk.Length; i++)
        {
            Assert.Equal(i, outChunk[i]);
        }
    }

    [Fact]
    public void Buffer_Overrun_ReturnsFalseAndIncrementsTelemetry()
    {
        var buffer = new LockFreeSpscRingBuffer(256);
        float[] bigInput = new float[300];

        bool written = buffer.TryWrite(bigInput);
        Assert.False(written);
        Assert.Equal(1, buffer.OverrunCount);
        Assert.Equal(0, buffer.AvailableRead());
    }

    [Fact]
    public void Buffer_Underrun_ReturnsFalseAndIncrementsTelemetry()
    {
        var buffer = new LockFreeSpscRingBuffer(256);
        float[] input = new float[50];
        buffer.TryWrite(input);

        float[] request = new float[100];
        bool read = buffer.TryRead(request);
        Assert.False(read);
        Assert.Equal(1, buffer.UnderrunCount);
        Assert.Equal(50, buffer.AvailableRead()); // Data preserved
    }

    [Fact]
    public void Buffer_ReadAvailable_ReturnsPartial()
    {
        var buffer = new LockFreeSpscRingBuffer(256);
        float[] input = new float[50];
        for (int i = 0; i < input.Length; i++) input[i] = 42.0f;
        buffer.TryWrite(input);

        float[] request = new float[100];
        int count = buffer.ReadAvailable(request);
        Assert.Equal(50, count);
        Assert.Equal(0, buffer.AvailableRead());
        Assert.Equal(42.0f, request[0]);
    }
}

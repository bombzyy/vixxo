using Vixxo.Core.Protocol;

namespace Vixxo.Tests;

public class VixxoFramingStreamTests
{
    // A stream wrapper that simulates network fragmentation by returning at most maxChunkSize bytes per Read
    private class FragmentedStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maxChunkSize;

        public FragmentedStream(byte[] data, int maxChunkSize = 1)
        {
            _inner = new MemoryStream(data);
            _maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = Math.Min(count, _maxChunkSize);
            return _inner.Read(buffer, offset, toRead);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int toRead = Math.Min(buffer.Length, _maxChunkSize);
            byte[] temp = new byte[toRead];
            int read = _inner.Read(temp, 0, toRead);
            temp.AsSpan(0, read).CopyTo(buffer.Span);
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Reader_HandlesExtremePartialReads_OneByteAtATime()
    {
        // Construct a frame: Ping with 8 bytes payload
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
        var header = new FrameHeader(MessageType.Ping, (uint)payload.Length);

        byte[] frameBytes = new byte[FrameHeader.HeaderSize + payload.Length];
        header.WriteTo(frameBytes.AsSpan(0, FrameHeader.HeaderSize));
        payload.CopyTo(frameBytes.AsSpan(FrameHeader.HeaderSize));

        // Read through stream that returns only 1 byte per Read call
        using var fragmentedStream = new FragmentedStream(frameBytes, maxChunkSize: 1);
        var reader = new VixxoFrameReader(fragmentedStream);

        var (readHeader, readPayload) = await reader.ReadFrameAsync();

        Assert.Equal(MessageType.Ping, readHeader.Type);
        Assert.Equal(8u, readHeader.PayloadLength);
        Assert.Equal(payload, readPayload);
    }

    [Fact]
    public async Task Reader_HandlesMultipleCoalescedFrames_InSingleStream()
    {
        using var memStream = new MemoryStream();
        var writer = new VixxoFrameWriter(memStream);

        // Write 3 consecutive frames
        await writer.WritePingAsync(1001);
        await writer.WritePingAsync(1002);
        await writer.WritePingAsync(1003);

        memStream.Position = 0;
        var reader = new VixxoFrameReader(memStream);

        var (h1, p1) = await reader.ReadFrameAsync();
        var (h2, p2) = await reader.ReadFrameAsync();
        var (h3, p3) = await reader.ReadFrameAsync();

        Assert.Equal(MessageType.Ping, h1.Type);
        Assert.Equal(MessageType.Ping, h2.Type);
        Assert.Equal(MessageType.Ping, h3.Type);

        ulong ts1 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(p1);
        ulong ts2 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(p2);
        ulong ts3 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(p3);

        Assert.Equal(1001ul, ts1);
        Assert.Equal(1002ul, ts2);
        Assert.Equal(1003ul, ts3);
    }

    [Fact]
    public async Task Writer_AudioFrameWriting_MatchesReaderExtraction()
    {
        using var memStream = new MemoryStream();
        var writer = new VixxoFrameWriter(memStream);

        var audioHeader = new AudioHeader(42, 1000000, 960, AudioFrameFlags.None);
        byte[] pcmData = new byte[1920];
        Array.Fill(pcmData, (byte)0x5A);

        await writer.WriteAudioFrameAsync(audioHeader, pcmData);

        memStream.Position = 0;
        var reader = new VixxoFrameReader(memStream);

        var (frameHeader, payload) = await reader.ReadFrameAsync();

        Assert.Equal(MessageType.Audio, frameHeader.Type);
        Assert.Equal((uint)(AudioHeader.HeaderSize + pcmData.Length), frameHeader.PayloadLength);

        Assert.True(AudioHeader.TryParse(payload.AsSpan(0, AudioHeader.HeaderSize), out var parsedAudioHeader, out _));
        Assert.Equal(42ul, parsedAudioHeader.SequenceNumber);
        Assert.Equal(960, parsedAudioHeader.SamplesPerChannel);

        byte[] extractedPcm = payload[AudioHeader.HeaderSize..];
        Assert.Equal(pcmData, extractedPcm);
    }
}

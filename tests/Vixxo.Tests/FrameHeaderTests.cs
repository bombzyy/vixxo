using Vixxo.Core.Protocol;

namespace Vixxo.Tests;

public class FrameHeaderTests
{
    [Fact]
    public void ValidHeader_SerializesAndParses_Correctly()
    {
        var header = new FrameHeader(MessageType.Hello, 1024);
        Span<byte> buffer = stackalloc byte[FrameHeader.HeaderSize];
        header.WriteTo(buffer);

        Assert.True(FrameHeader.TryParse(buffer, out var parsed, out var error), error);
        Assert.Equal(MessageType.Hello, parsed.Type);
        Assert.Equal(1024u, parsed.PayloadLength);
    }

    [Fact]
    public void InvalidMagic_IsRejected()
    {
        byte[] buffer = new byte[8];
        buffer[0] = (byte)'X';
        buffer[1] = (byte)'Y';
        buffer[2] = 0x01;
        buffer[3] = (byte)MessageType.Ping;

        bool success = FrameHeader.TryParse(buffer, out _, out var error);
        Assert.False(success);
        Assert.Contains("Invalid magic bytes", error);
    }

    [Fact]
    public void UnsupportedVersion_IsRejected()
    {
        byte[] buffer = new byte[8];
        buffer[0] = (byte)'P';
        buffer[1] = (byte)'M';
        buffer[2] = 0x02; // Version 2
        buffer[3] = (byte)MessageType.Ping;

        bool success = FrameHeader.TryParse(buffer, out _, out var error);
        Assert.False(success);
        Assert.Contains("Unsupported protocol version", error);
    }

    [Fact]
    public void ControlPayload_ExceedingLimit_IsRejected()
    {
        byte[] buffer = new byte[8];
        buffer[0] = (byte)'P';
        buffer[1] = (byte)'M';
        buffer[2] = 0x01;
        buffer[3] = (byte)MessageType.Hello;
        // 65537 bytes (limit is 65536)
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), 65537);

        bool success = FrameHeader.TryParse(buffer, out _, out var error);
        Assert.False(success);
        Assert.Contains("exceeds maximum limit", error);
    }

    [Fact]
    public void AudioPayload_ExceedingLimit_IsRejected()
    {
        byte[] buffer = new byte[8];
        buffer[0] = (byte)'P';
        buffer[1] = (byte)'M';
        buffer[2] = 0x01;
        buffer[3] = (byte)MessageType.Audio;
        // 16385 bytes (limit is 16384)
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), 16385);

        bool success = FrameHeader.TryParse(buffer, out _, out var error);
        Assert.False(success);
        Assert.Contains("exceeds maximum limit", error);
    }
}

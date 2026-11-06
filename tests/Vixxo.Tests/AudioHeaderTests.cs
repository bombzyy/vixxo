using Vixxo.Core.Protocol;

namespace Vixxo.Tests;

public class AudioHeaderTests
{
    [Fact]
    public void ValidAudioHeader_SerializesAndParses_Correctly()
    {
        ulong seq = 123456789012345;
        ulong ts = 9876543210;
        ushort samples = 960;
        AudioFrameFlags flags = AudioFrameFlags.SyntheticTestTone | AudioFrameFlags.Muted;

        var header = new AudioHeader(seq, ts, samples, flags);
        Span<byte> buffer = stackalloc byte[AudioHeader.HeaderSize];
        header.WriteTo(buffer);

        Assert.True(AudioHeader.TryParse(buffer, out var parsed, out var error), error);
        Assert.Equal(seq, parsed.SequenceNumber);
        Assert.Equal(ts, parsed.TimestampUs);
        Assert.Equal(samples, parsed.SamplesPerChannel);
        Assert.Equal(flags, parsed.Flags);
    }

    [Fact]
    public void SmallBuffer_FailsParsing()
    {
        byte[] tooSmall = new byte[19]; // HeaderSize is 20
        bool success = AudioHeader.TryParse(tooSmall, out _, out var error);
        Assert.False(success);
        Assert.Contains("smaller than 20-byte", error);
    }
}

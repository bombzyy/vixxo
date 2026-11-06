using System.Buffers.Binary;

namespace Vixxo.Core.Protocol;

[Flags]
public enum AudioFrameFlags : ushort
{
    None = 0x0000,
    Muted = 0x0001,
    Discontinuity = 0x0002,
    SyntheticTestTone = 0x0004
}

public readonly record struct AudioHeader(
    ulong SequenceNumber,
    ulong TimestampUs,
    ushort SamplesPerChannel,
    AudioFrameFlags Flags)
{
    public const int HeaderSize = 20;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination span too small for audio header", nameof(destination));

        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(0, 8), SequenceNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), TimestampUs);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(16, 2), SamplesPerChannel);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(18, 2), (ushort)Flags);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out AudioHeader header, out string? error)
    {
        header = default;
        error = null;

        if (source.Length < HeaderSize)
        {
            error = "Source buffer smaller than 20-byte audio header.";
            return false;
        }

        ulong seq = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(0, 8));
        ulong ts = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(8, 8));
        ushort samples = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(16, 2));
        ushort flagsRaw = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(18, 2));

        header = new AudioHeader(seq, ts, samples, (AudioFrameFlags)flagsRaw);
        return true;
    }
}

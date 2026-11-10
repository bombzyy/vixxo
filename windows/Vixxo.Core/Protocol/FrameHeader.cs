namespace Vixxo.Core.Protocol;

public enum MessageType : byte
{
    Hello = 0x01,
    Auth = 0x02,
    Welcome = 0x03,
    Config = 0x04,
    Start = 0x05,
    Stop = 0x06,
    Audio = 0x07,
    Ping = 0x08,
    Pong = 0x09,
    Stats = 0x0A,
    Control = 0x0B,
    Error = 0x0C,
    Bye = 0x0D,
    SoundboardSync = 0x0E,
    SoundboardAction = 0x0F
}

public readonly record struct FrameHeader(MessageType Type, uint PayloadLength)
{
    public const ushort ExpectedMagic = 0x4D50; // 'P' (0x50) | ('M' (0x4D) << 8) in Little-Endian
    public const byte ExpectedVersion = 0x01;
    public const int HeaderSize = 8;
    public const uint MaxControlPayloadLength = 64 * 1024;    // 64 KB
    public const uint MaxAudioPayloadLength = 16 * 1024;      // 16 KB

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination span too small for header", nameof(destination));

        destination[0] = (byte)'P';
        destination[1] = (byte)'M';
        destination[2] = ExpectedVersion;
        destination[3] = (byte)Type;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), PayloadLength);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out FrameHeader header, out string? error)
    {
        header = default;
        error = null;

        if (source.Length < HeaderSize)
        {
            error = "Source buffer smaller than 8-byte header.";
            return false;
        }

        if (source[0] != (byte)'P' || source[1] != (byte)'M')
        {
            error = $"Invalid magic bytes: 0x{source[0]:X2} 0x{source[1]:X2}. Expected 'P' 'M'.";
            return false;
        }

        byte version = source[2];
        if (version != ExpectedVersion)
        {
            error = $"Unsupported protocol version {version}. Expected {ExpectedVersion}.";
            return false;
        }

        byte typeByte = source[3];
        if (!Enum.IsDefined(typeof(MessageType), typeByte))
        {
            error = $"Unknown message type code 0x{typeByte:X2}.";
            return false;
        }

        MessageType type = (MessageType)typeByte;
        uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4));

        uint maxAllowed = (type == MessageType.Audio) ? MaxAudioPayloadLength : MaxControlPayloadLength;
        if (length > maxAllowed)
        {
            error = $"Payload length {length} exceeds maximum limit of {maxAllowed} for message type {type}.";
            return false;
        }

        header = new FrameHeader(type, length);
        return true;
    }
}

using System.Buffers;

namespace Vixxo.Core.Protocol;

public class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
    public ProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class VixxoFrameReader
{
    private readonly Stream _stream;
    private readonly byte[] _headerBuffer = new byte[FrameHeader.HeaderSize];

    public VixxoFrameReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async ValueTask<(FrameHeader Header, byte[] Payload)> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        // Read exactly 8 bytes of header
        await ReadExactBytesAsync(_stream, _headerBuffer.AsMemory(0, FrameHeader.HeaderSize), cancellationToken).ConfigureAwait(false);

        if (!FrameHeader.TryParse(_headerBuffer, out var header, out var error))
        {
            throw new ProtocolException($"Failed to parse frame header: {error}");
        }

        byte[] payload = header.PayloadLength > 0 ? new byte[header.PayloadLength] : Array.Empty<byte>();

        if (header.PayloadLength > 0)
        {
            await ReadExactBytesAsync(_stream, payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        return (header, payload);
    }

    public static async ValueTask ReadExactBytesAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Expected {buffer.Length} bytes, but stream closed after reading {totalRead} bytes.");
            }
            totalRead += read;
        }
    }
}

using System.Buffers;
using System.Buffers.Binary;

namespace Vixxo.Core.Protocol;

public sealed class VixxoFrameWriter : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _headerBuffer = new byte[FrameHeader.HeaderSize];
    private bool _disposed;

    public VixxoFrameWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async ValueTask WriteFrameAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var header = new FrameHeader(type, (uint)payload.Length);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            header.WriteTo(_headerBuffer);
            await _stream.WriteAsync(_headerBuffer.AsMemory(0, FrameHeader.HeaderSize), cancellationToken).ConfigureAwait(false);
            if (payload.Length > 0)
            {
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public ValueTask WriteJsonAsync<T>(MessageType type, T value, CancellationToken cancellationToken = default)
    {
        byte[] jsonBytes = ProtocolJson.Serialize(value);
        return WriteFrameAsync(type, jsonBytes, cancellationToken);
    }

    public async ValueTask WriteAudioFrameAsync(AudioHeader audioHeader, ReadOnlyMemory<byte> pcmData, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalPayloadLength = AudioHeader.HeaderSize + pcmData.Length;
        var frameHeader = new FrameHeader(MessageType.Audio, (uint)totalPayloadLength);

        byte[] combinedBuffer = ArrayPool<byte>.Shared.Rent(FrameHeader.HeaderSize + totalPayloadLength);
        try
        {
            frameHeader.WriteTo(combinedBuffer.AsSpan(0, FrameHeader.HeaderSize));
            audioHeader.WriteTo(combinedBuffer.AsSpan(FrameHeader.HeaderSize, AudioHeader.HeaderSize));
            pcmData.Span.CopyTo(combinedBuffer.AsSpan(FrameHeader.HeaderSize + AudioHeader.HeaderSize, pcmData.Length));

            int totalWriteLength = FrameHeader.HeaderSize + totalPayloadLength;

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(combinedBuffer.AsMemory(0, totalWriteLength), cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(combinedBuffer);
        }
    }

    public ValueTask WritePingAsync(ulong timestampUs, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, timestampUs);
        return WriteFrameAsync(MessageType.Ping, payload, cancellationToken);
    }

    public ValueTask WritePongAsync(ulong timestampUs, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, timestampUs);
        return WriteFrameAsync(MessageType.Pong, payload, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writeLock.Dispose();
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}

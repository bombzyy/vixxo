using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vixxo.Core.Protocol;

public sealed record AudioFormatDescriptor(
    [property: JsonPropertyName("sampleRate")] int SampleRate = 48000,
    [property: JsonPropertyName("channels")] int Channels = 1,
    [property: JsonPropertyName("bitDepth")] int BitDepth = 16,
    [property: JsonPropertyName("frameSizeMs")] int FrameSizeMs = 20)
{
    [JsonIgnore]
    public int BytesPerSample => (BitDepth / 8) * Channels;

    [JsonIgnore]
    public int SamplesPerFrame => (SampleRate * FrameSizeMs) / 1000;

    [JsonIgnore]
    public int BytesPerFrame => SamplesPerFrame * BytesPerSample;
}

public sealed record HelloPayload(
    [property: JsonPropertyName("clientName")] string ClientName,
    [property: JsonPropertyName("clientVersion")] string ClientVersion,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("supportedFormats")] List<AudioFormatDescriptor> SupportedFormats);

public sealed record AuthPayload(
    [property: JsonPropertyName("authMethod")] string AuthMethod, // "pairingToken" | "pin" | "deviceCredential" | "testSender"
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("deviceId")] string DeviceId);

public sealed record WelcomePayload(
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("serverName")] string ServerName,
    [property: JsonPropertyName("deviceCredential")] string? DeviceCredential,
    [property: JsonPropertyName("activeAudioFormat")] AudioFormatDescriptor ActiveAudioFormat);

public sealed record ConfigPayload(
    [property: JsonPropertyName("sampleRate")] int SampleRate,
    [property: JsonPropertyName("channels")] int Channels,
    [property: JsonPropertyName("bitDepth")] int BitDepth,
    [property: JsonPropertyName("frameSizeMs")] int FrameSizeMs,
    [property: JsonPropertyName("latencyMode")] string LatencyMode);

public sealed record StartPayload(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("timestamp")] long Timestamp);

public sealed record StopPayload(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("timestamp")] long Timestamp);

public sealed record ControlPayload(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("isMuted")] bool? IsMuted,
    [property: JsonPropertyName("gainDb")] float? GainDb);

public sealed record ErrorPayload(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("recoverable")] bool Recoverable);

public sealed record StatsPayload(
    [property: JsonPropertyName("bufferDepthMs")] float BufferDepthMs,
    [property: JsonPropertyName("rttMs")] float RttMs,
    [property: JsonPropertyName("underrunCount")] long UnderrunCount,
    [property: JsonPropertyName("dropCount")] long DropCount);

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Bytes) =>
        JsonSerializer.Deserialize<T>(utf8Bytes, Options);
}

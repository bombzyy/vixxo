# Vixxo Protocol Specification (v1.0)

## 1. Overview
The Vixxo protocol defines communication between an Android audio capture client and a Windows audio receiver server over TCP (with optional TLS) and UDP (for LAN discovery).

- **Default TCP Control & Audio Port**: `47800`
- **Default UDP Discovery Port**: `47801`
- **Byte Order**: Little-Endian for all multi-byte numeric primitives unless otherwise stated.
- **String Encoding**: UTF-8 without BOM.

---

## 2. Framing Header (8 Bytes Fixed)

Every message transmitted over TCP begins with an 8-byte fixed header:

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Magic 'P' (0x50)        |       Magic 'M' (0x4D)        |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Version (1)  |  Type (1..13) |        Payload Length         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Payload Length (cont.)  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Fields
| Offset | Field | Type | Description |
| :--- | :--- | :--- | :--- |
| `0..1` | `Magic` | `uint16` / 2 bytes | Fixed ASCII characters `'P'` (`0x50`), `'M'` (`0x4D`). |
| `2` | `Version` | `uint8` | Current protocol version: `0x01`. |
| `3` | `Type` | `uint8` | Message type numeric code (`0x01`–`0x0D`). |
| `4..7` | `PayloadLength` | `uint32` (LE) | Size of the message payload in bytes (0 to MaxPayloadSize). |

### Payload Size Limits
- Maximum Control Message Payload: **65,536 bytes (64 KB)**
- Maximum Audio Message Payload: **16,384 bytes (16 KB)** (a typical 20 ms 48 kHz mono frame is 1920 bytes + 20 bytes audio header = 1940 bytes)
- Any frame exceeding these limits must be immediately rejected and trigger connection termination.

---

## 3. Message Types

| Value | Name | Direction | Payload Format | Description |
| :--- | :--- | :--- | :--- | :--- |
| `0x01` | `HELLO` | Client -> Server | UTF-8 JSON | Initial handshake message with client info. |
| `0x02` | `AUTH` | Client -> Server | UTF-8 JSON | Authentication credentials (pairing token, PIN, or device secret). |
| `0x03` | `WELCOME` | Server -> Client | UTF-8 JSON | Authentication confirmation and session parameters. |
| `0x04` | `CONFIG` | Server <-> Client | UTF-8 JSON | Negotiation of audio format, frame size, and latency mode. |
| `0x05` | `START` | Client -> Server | UTF-8 JSON | Request to begin microphone streaming. |
| `0x06` | `STOP` | Client <-> Server | UTF-8 JSON | Request to stop streaming. |
| `0x07` | `AUDIO` | Client -> Server | Binary | Real-time PCM audio frame. |
| `0x08` | `PING` | Bidirectional | Binary (8-byte timestamp) | Latency and keep-alive probe. |
| `0x09` | `PONG` | Bidirectional | Binary (8-byte timestamp) | Response echoing original probe timestamp. |
| `0x0A` | `STATS` | Bidirectional | UTF-8 JSON | Runtime telemetry (buffer depth, RTT, underruns, drops). |
| `0x0B` | `CONTROL` | Client <-> Server | UTF-8 JSON | Remote control actions (mute, gain adjust). |
| `0x0C` | `ERROR` | Bidirectional | UTF-8 JSON | Error notification with structured code and message. |
| `0x0D` | `BYE` | Bidirectional | UTF-8 JSON | Graceful connection teardown. |

---

## 4. Message Payloads (Schemas)

### 4.1 HELLO (`0x01`)
Sent by Android client upon TCP connection:
```json
{
  "clientName": "Pixel 7 Pro",
  "clientVersion": "1.0.0",
  "protocolVersion": 1,
  "deviceId": "550e8400-e29b-41d4-a716-446655440000",
  "supportedFormats": [
    { "sampleRate": 48000, "channels": 1, "bitDepth": 16, "frameSizeMs": 20 },
    { "sampleRate": 44100, "channels": 1, "bitDepth": 16, "frameSizeMs": 20 }
  ]
}
```

### 4.2 AUTH (`0x02`)
Client authentication attempt:
```json
{
  "authMethod": "pairingToken", // "pairingToken" | "pin" | "deviceCredential"
  "token": "dGhpcy1pcy1hLXJhbmRvbS10b2tlbg==",
  "deviceId": "550e8400-e29b-41d4-a716-446655440000"
}
```

### 4.3 WELCOME (`0x03`)
Server acceptance:
```json
{
  "authenticated": true,
  "sessionId": "b8630048-b4b1-4b71-9f9b-63a562145e12",
  "serverName": "My-Windows-PC",
  "deviceCredential": "issued-permanent-token-if-pairing",
  "activeAudioFormat": {
    "sampleRate": 48000,
    "channels": 1,
    "bitDepth": 16,
    "frameSizeMs": 20
  }
}
```

### 4.4 CONFIG (`0x04`)
Format negotiation or latency mode update:
```json
{
  "sampleRate": 48000,
  "channels": 1,
  "bitDepth": 16,
  "frameSizeMs": 20,
  "latencyMode": "balanced" // "low" | "balanced" | "stable"
}
```

### 4.5 START (`0x05`) & STOP (`0x06`)
```json
{
  "reason": "user_initiated",
  "timestamp": 1730900000000
}
```

### 4.6 AUDIO Frame (`0x07`) - Binary Layout
The payload for type `0x07` consists of a 20-byte audio header followed by raw PCM samples:

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Sequence Number                        |
|                           (uint64)                            |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                  Capture Monotonic Timestamp                  |
|                        (uint64, micros)                       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|      Samples Per Channel      |            Flags              |
|           (uint16)            |           (uint16)            |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Raw PCM Bytes                          |
|             (Signed 16-bit little-endian samples)             |
|                              ...                              |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

- **Sequence Number (`uint64`, LE)**: Starts at 0 for each stream, increments by 1 per frame. Wraps naturally at $2^{64}-1$.
- **Capture Timestamp (`uint64`, LE)**: Microseconds from phone's monotonic clock (`SystemClock.elapsedRealtimeNanos() / 1000`).
- **Samples Per Channel (`uint16`, LE)**: Typically 960 for 20 ms at 48 kHz.
- **Flags (`uint16`, LE)**:
  - Bit 0: Muted (`0x0001`)
  - Bit 1: Discontinuity / Reconnect (`0x0002`)
  - Bit 2: Synthetic / Test tone (`0x0004`)
- **Raw PCM Bytes**: `SamplesPerChannel * Channels * (BitDepth / 8)` bytes.
  - Standard frame size = $960 \times 1 \times 2 = 1920$ bytes.
  - Total frame payload = $20 + 1920 = 1940$ bytes.

### 4.7 PING (`0x08`) & PONG (`0x09`)
Payload: 8 bytes (`uint64` LE), containing the sender's monotonic timestamp in microseconds. The receiver immediately replies with PONG echoing the exact same 8 bytes.

### 4.8 CONTROL (`0x0B`)
```json
{
  "action": "setMute", // "setMute" | "setGain"
  "isMuted": true,
  "gainDb": 3.0
}
```

### 4.9 ERROR (`0x0C`)
```json
{
  "code": 401,
  "category": "AUTH_FAILED",
  "message": "Invalid pairing token or expired PIN.",
  "recoverable": false
}
```

---

## 5. UDP Discovery Protocol

Windows PC listens on UDP port `47801` and broadcasts or responds to discovery requests.

### Discovery Request (Client -> Broadcast `255.255.255.255:47801`):
```json
{
  "magic": "PM_DISCOVER",
  "version": 1,
  "nonce": "a1b2c3d4"
}
```

### Discovery Response (Server -> Client):
```json
{
  "magic": "PM_OFFER",
  "version": 1,
  "nonce": "a1b2c3d4",
  "serverName": "My-Windows-PC",
  "tcpPort": 47800,
  "spkiFingerprint": "SHA256:dGhpcyBpcyBhIGZpbmdlcnByaW50...",
  "inUse": false
}
```

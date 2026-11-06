package com.vixxo.mic.network

import java.nio.ByteBuffer
import java.nio.ByteOrder

enum class MessageType(val value: Byte) {
    HELLO(0x01),
    AUTH(0x02),
    WELCOME(0x03),
    CONFIG(0x04),
    START(0x05),
    STOP(0x06),
    AUDIO(0x07),
    PING(0x08),
    PONG(0x09),
    STATS(0x0A),
    CONTROL(0x0B),
    ERROR(0x0C),
    BYE(0x0D);

    companion object {
        fun fromByte(b: Byte): MessageType? = entries.find { it.value == b }
    }
}

data class FrameHeader(
    val type: MessageType,
    val payloadLength: Int
) {
    companion object {
        const val HEADER_SIZE = 8
        const val MAGIC_P: Byte = 0x50 // 'P'
        const val MAGIC_M: Byte = 0x4D // 'M'
        const val PROTOCOL_VERSION: Byte = 0x01
        const val MAX_CONTROL_PAYLOAD = 64 * 1024
        const val MAX_AUDIO_PAYLOAD = 16 * 1024

        fun parse(buffer: ByteArray, offset: Int = 0): Result<FrameHeader> {
            if (buffer.size - offset < HEADER_SIZE) {
                return Result.failure(IllegalArgumentException("Buffer smaller than 8-byte header"))
            }

            if (buffer[offset] != MAGIC_P || buffer[offset + 1] != MAGIC_M) {
                return Result.failure(IllegalStateException("Invalid magic bytes"))
            }

            val version = buffer[offset + 2]
            if (version != PROTOCOL_VERSION) {
                return Result.failure(IllegalStateException("Unsupported protocol version $version"))
            }

            val typeByte = buffer[offset + 3]
            val type = MessageType.fromByte(typeByte)
                ?: return Result.failure(IllegalStateException("Unknown message type $typeByte"))

            val byteBuffer = ByteBuffer.wrap(buffer, offset + 4, 4).order(ByteOrder.LITTLE_ENDIAN)
            val length = byteBuffer.int

            val maxAllowed = if (type == MessageType.AUDIO) MAX_AUDIO_PAYLOAD else MAX_CONTROL_PAYLOAD
            if (length < 0 || length > maxAllowed) {
                return Result.failure(IllegalStateException("Payload length $length exceeds limit $maxAllowed"))
            }

            return Result.success(FrameHeader(type, length))
        }
    }

    fun writeTo(buffer: ByteArray, offset: Int = 0) {
        buffer[offset] = MAGIC_P
        buffer[offset + 1] = MAGIC_M
        buffer[offset + 2] = PROTOCOL_VERSION
        buffer[offset + 3] = type.value
        ByteBuffer.wrap(buffer, offset + 4, 4)
            .order(ByteOrder.LITTLE_ENDIAN)
            .putInt(payloadLength)
    }
}

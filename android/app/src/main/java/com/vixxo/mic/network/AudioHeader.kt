package com.vixxo.mic.network

import java.nio.ByteBuffer
import java.nio.ByteOrder

data class AudioHeader(
    val sequenceNumber: Long,
    val timestampUs: Long,
    val samplesPerChannel: Short,
    val flags: Short
) {
    companion object {
        const val HEADER_SIZE = 20
        const val FLAG_MUTED: Short = 0x0001
        const val FLAG_DISCONTINUITY: Short = 0x0002
        const val FLAG_SYNTHETIC: Short = 0x0004

        fun parse(buffer: ByteArray, offset: Int = 0): Result<AudioHeader> {
            if (buffer.size - offset < HEADER_SIZE) {
                return Result.failure(IllegalArgumentException("Buffer smaller than 20-byte audio header"))
            }

            val bb = ByteBuffer.wrap(buffer, offset, HEADER_SIZE).order(ByteOrder.LITTLE_ENDIAN)
            val seq = bb.long
            val ts = bb.long
            val samples = bb.short
            val flags = bb.short

            return Result.success(AudioHeader(seq, ts, samples, flags))
        }
    }

    fun writeTo(buffer: ByteArray, offset: Int = 0) {
        ByteBuffer.wrap(buffer, offset, HEADER_SIZE)
            .order(ByteOrder.LITTLE_ENDIAN)
            .putLong(sequenceNumber)
            .putLong(timestampUs)
            .putShort(samplesPerChannel)
            .putShort(flags)
    }
}

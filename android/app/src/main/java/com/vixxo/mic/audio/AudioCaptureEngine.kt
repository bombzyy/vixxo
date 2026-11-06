package com.vixxo.mic.audio

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.media.audiofx.AcousticEchoCanceler
import android.media.audiofx.AutomaticGainControl
import android.media.audiofx.NoiseSuppressor
import android.os.Process
import android.os.SystemClock
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.abs
import kotlin.math.sqrt

data class AudioChunk(
    val sequenceNumber: Long,
    val timestampUs: Long,
    val data: ByteArray,
    val length: Int,
    val isMuted: Boolean
)

class AudioCaptureEngine(
    private val sampleRate: Int = 48000,
    private val frameSizeMs: Int = 20
) {
    private var audioRecord: AudioRecord? = null
    private var noiseSuppressor: NoiseSuppressor? = null
    private var echoCanceler: AcousticEchoCanceler? = null
    private var gainControl: AutomaticGainControl? = null

    private var captureThread: Thread? = null
    private val isRunning = AtomicBoolean(false)

    // Bounded queue: hold at most 20 frames (400 ms) to keep latency strictly bounded
    val audioQueue = ArrayBlockingQueue<AudioChunk>(20)

    @Volatile
    var isMuted: Boolean = false

    @Volatile
    var softwareGain: Float = 1.0f

    @Volatile
    var currentPeakLevel: Float = 0f
        private set

    @Volatile
    var currentRmsLevel: Float = 0f
        private set

    @Volatile
    var framesCaptured: Long = 0
        private set

    @SuppressLint("MissingPermission")
    fun start(): Result<Unit> {
        if (isRunning.get()) return Result.success(Unit)

        val channelConfig = AudioFormat.CHANNEL_IN_MONO
        val audioFormat = AudioFormat.ENCODING_PCM_16BIT
        val samplesPerFrame = (sampleRate * frameSizeMs) / 1000 // 960 samples for 20 ms @ 48kHz
        val bytesPerFrame = samplesPerFrame * 2 // 1920 bytes

        val minBufSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)
        if (minBufSize <= 0) {
            return Result.failure(IllegalStateException("Invalid AudioRecord minBufferSize: $minBufSize"))
        }

        val bufferSize = maxOf(minBufSize, bytesPerFrame * 4)

        // Try VOICE_COMMUNICATION first, fallback to MIC
        var record: AudioRecord? = null
        try {
            record = AudioRecord(
                MediaRecorder.AudioSource.VOICE_COMMUNICATION,
                sampleRate,
                channelConfig,
                audioFormat,
                bufferSize
            )
        } catch (_: Exception) {}

        if (record == null || record.state != AudioRecord.STATE_INITIALIZED) {
            record?.release()
            record = AudioRecord(
                MediaRecorder.AudioSource.MIC,
                sampleRate,
                channelConfig,
                audioFormat,
                bufferSize
            )
        }

        if (record.state != AudioRecord.STATE_INITIALIZED) {
            record.release()
            return Result.failure(IllegalStateException("AudioRecord failed to initialize with VOICE_COMMUNICATION or MIC"))
        }

        // Enable hardware audio enhancements where supported
        val audioSessionId = record.audioSessionId
        if (audioSessionId != 0) {
            if (NoiseSuppressor.isAvailable()) {
                try {
                    noiseSuppressor = NoiseSuppressor.create(audioSessionId)?.apply { enabled = true }
                } catch (_: Exception) {}
            }
            if (AcousticEchoCanceler.isAvailable()) {
                try {
                    echoCanceler = AcousticEchoCanceler.create(audioSessionId)?.apply { enabled = true }
                } catch (_: Exception) {}
            }
            if (AutomaticGainControl.isAvailable()) {
                try {
                    gainControl = AutomaticGainControl.create(audioSessionId)?.apply { enabled = true }
                } catch (_: Exception) {}
            }
        }

        audioRecord = record
        isRunning.set(true)
        audioQueue.clear()
        framesCaptured = 0

        captureThread = Thread({
            Process.setThreadPriority(Process.THREAD_PRIORITY_URGENT_AUDIO)

            try {
                record.startRecording()
            } catch (e: Exception) {
                isRunning.set(false)
                return@Thread
            }

            val readBuffer = ByteArray(bytesPerFrame)
            var seq = 0L

            while (isRunning.get()) {
                var bytesRead = 0
                while (bytesRead < bytesPerFrame && isRunning.get()) {
                    val read = record.read(readBuffer, bytesRead, bytesPerFrame - bytesRead)
                    if (read > 0) {
                        bytesRead += read
                    } else if (read == AudioRecord.ERROR_INVALID_OPERATION || read == AudioRecord.ERROR_BAD_VALUE) {
                        break
                    }
                }

                if (bytesRead < bytesPerFrame || !isRunning.get()) continue

                val timestampUs = SystemClock.elapsedRealtimeNanos() / 1000

                // Software Gain, Limiter, and Metering
                var maxSample = 0
                var sumSquares = 0.0
                val samplesCount = bytesPerFrame / 2

                val chunkData = ByteArray(bytesPerFrame)

                for (i in 0 until samplesCount) {
                    val low = readBuffer[i * 2].toInt() and 0xFF
                    val high = readBuffer[i * 2 + 1].toInt()
                    var sample = (high shl 8) or low

                    // Convert to signed 16-bit
                    if (sample >= 32768) sample -= 65536

                    if (isMuted) {
                        sample = 0
                    } else if (abs(softwareGain - 1.0f) > 0.01f) {
                        var scaled = (sample * softwareGain).toInt()
                        if (scaled > 32767) scaled = 32767
                        if (scaled < -32768) scaled = -32768
                        sample = scaled
                    }

                    val absSample = abs(sample)
                    if (absSample > maxSample) maxSample = absSample
                    val norm = absSample / 32768.0
                    sumSquares += norm * norm

                    chunkData[i * 2] = (sample and 0xFF).toByte()
                    chunkData[i * 2 + 1] = ((sample shr 8) and 0xFF).toByte()
                }

                currentPeakLevel = maxSample / 32768.0f
                currentRmsLevel = sqrt(sumSquares / samplesCount).toFloat()

                // Offer to bounded queue; if full, remove oldest to prevent latency buildup
                if (!audioQueue.offer(AudioChunk(seq++, timestampUs, chunkData, bytesPerFrame, isMuted))) {
                    audioQueue.poll() // drop oldest
                    audioQueue.offer(AudioChunk(seq, timestampUs, chunkData, bytesPerFrame, isMuted))
                }
                framesCaptured++
            }
        }, "Vixxo-AudioCapture").apply {
            start()
        }

        return Result.success(Unit)
    }

    fun stop() {
        if (!isRunning.getAndSet(false)) return

        captureThread?.interrupt()
        captureThread = null

        try {
            audioRecord?.stop()
        } catch (_: Exception) {}

        try { audioRecord?.release() } catch (_: Exception) {}
        try { noiseSuppressor?.release() } catch (_: Exception) {}
        try { echoCanceler?.release() } catch (_: Exception) {}
        try { gainControl?.release() } catch (_: Exception) {}

        audioRecord = null
        noiseSuppressor = null
        echoCanceler = null
        gainControl = null
        audioQueue.clear()
        currentPeakLevel = 0f
        currentRmsLevel = 0f
    }
}

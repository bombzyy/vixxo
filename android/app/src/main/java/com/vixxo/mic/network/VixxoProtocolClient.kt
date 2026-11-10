package com.vixxo.mic.network

import android.os.SystemClock
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

class VixxoProtocolClient(
    private val host: String,
    private val port: Int,
    private val deviceId: String,
    private val clientName: String = "Android-Phone",
    private val pairingPinOrToken: String? = null,
    private val deviceCredential: String? = null,
    private val pinnedFingerprint: String? = null,
    private val onCredentialReceived: ((String) -> Unit)? = null
) {
    private var rawSocket: Socket? = null
    private var sslSocket: SSLSocket? = null
    private var inputStream: InputStream? = null
    private var outputStream: OutputStream? = null

    private val headerBuffer = ByteArray(FrameHeader.HEADER_SIZE)
    private val audioHeaderBuffer = ByteArray(AudioHeader.HEADER_SIZE)
    private val writeLock = Any()

    @Volatile
    var isConnected: Boolean = false
        private set

    @Volatile
    var lastRttMs: Float = 0f
        private set

    var onSoundboardSyncReceived: ((SoundboardSyncData) -> Unit)? = null

    suspend fun connect(timeoutMs: Int = 5000): Result<String> = withContext(Dispatchers.IO) {
        try {
            val sock = Socket()
            sock.tcpNoDelay = true
            sock.connect(InetSocketAddress(host, port), timeoutMs)
            rawSocket = sock

            // Setup TLS if pinned fingerprint is provided, or try plain TCP
            if (!pinnedFingerprint.isNullOrBlank()) {
                val trustManager = object : X509TrustManager {
                    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
                    override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
                    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                        if (chain.isNullOrEmpty()) {
                            throw CertificateException("Server returned empty certificate chain")
                        }
                        val serverCert = chain[0]
                        val md = MessageDigest.getInstance("SHA-256")
                        val certHash = md.digest(serverCert.encoded)
                        val certFp = certHash.joinToString(":") { "%02X".format(it) }

                        val cleanPinned = pinnedFingerprint.replace(":", "").uppercase()
                        val cleanCert = certFp.replace(":", "").uppercase()

                        if (cleanCert != cleanPinned) {
                            throw CertificateException("Pinned certificate mismatch! Expected $pinnedFingerprint, got $certFp")
                        }
                    }
                }

                val sslContext = SSLContext.getInstance("TLS").apply {
                    init(null, arrayOf<TrustManager>(trustManager), null)
                }

                val ssl = sslContext.socketFactory.createSocket(sock, host, port, true) as SSLSocket
                ssl.tcpNoDelay = true
                ssl.startHandshake()
                sslSocket = ssl
                inputStream = ssl.inputStream
                outputStream = ssl.outputStream
            } else {
                inputStream = sock.getInputStream()
                outputStream = sock.getOutputStream()
            }

            // 1. Send HELLO
            val helloJson = JSONObject().apply {
                put("clientName", clientName)
                put("clientVersion", "1.0.0")
                put("protocolVersion", 1)
                put("deviceId", deviceId)
                put("supportedFormats", JSONArray().apply {
                    put(JSONObject().apply {
                        put("sampleRate", 48000)
                        put("channels", 1)
                        put("bitDepth", 16)
                        put("frameSizeMs", 20)
                    })
                })
            }
            writeJson(MessageType.HELLO, helloJson.toString())

            // 2. Send AUTH
            val authJson = JSONObject().apply {
                when {
                    !pairingPinOrToken.isNullOrBlank() -> {
                        put("authMethod", "pairingToken")
                        put("token", pairingPinOrToken)
                    }
                    !deviceCredential.isNullOrBlank() -> {
                        put("authMethod", "deviceCredential")
                        put("token", deviceCredential)
                    }
                    else -> {
                        put("authMethod", "testSender")
                        put("token", "android-direct-token")
                    }
                }
                put("deviceId", deviceId)
            }
            writeJson(MessageType.AUTH, authJson.toString())

            // 3. Receive WELCOME (or ERROR)
            val (respHeader, respPayload) = readFrame()
            if (respHeader.type == MessageType.ERROR) {
                val errObj = JSONObject(String(respPayload, Charsets.UTF_8))
                val errMsg = errObj.optString("message", "Authentication rejected by Windows PC")
                return@withContext Result.failure(SecurityException("Auth rejected: $errMsg"))
            }

            if (respHeader.type != MessageType.WELCOME) {
                return@withContext Result.failure(IllegalStateException("Expected WELCOME, received ${respHeader.type}"))
            }

            val welcomeObj = JSONObject(String(respPayload, Charsets.UTF_8))
            val serverName = welcomeObj.optString("serverName", "Windows-PC")

            // Store new device credential if issued
            val newCred = if (welcomeObj.has("deviceCredential") && !welcomeObj.isNull("deviceCredential")) welcomeObj.getString("deviceCredential") else null
            if (!newCred.isNullOrBlank()) {
                onCredentialReceived?.invoke(newCred)
            }

            // 4. Send START
            val startJson = JSONObject().apply {
                put("reason", "user_start")
                put("timestamp", System.currentTimeMillis())
            }
            writeJson(MessageType.START, startJson.toString())

            isConnected = true
            Result.success(serverName)
        } catch (e: Exception) {
            disconnect()
            Result.failure(e)
        }
    }

    fun sendAudioFrame(
        seq: Long,
        timestampUs: Long,
        pcmData: ByteArray,
        offset: Int,
        length: Int,
        isMuted: Boolean
    ) {
        if (!isConnected) return

        val out = outputStream ?: return
        val samplesCount = (length / 2).toShort()
        var flags: Short = 0
        if (isMuted) flags = (flags.toInt() or AudioHeader.FLAG_MUTED.toInt()).toShort()

        val audioHeader = AudioHeader(seq, timestampUs, samplesCount, flags)
        val totalPayloadLength = AudioHeader.HEADER_SIZE + length
        val frameHeader = FrameHeader(MessageType.AUDIO, totalPayloadLength)

        val frameTotalSize = FrameHeader.HEADER_SIZE + totalPayloadLength
        val sendBuffer = ByteArray(frameTotalSize)

        frameHeader.writeTo(sendBuffer, 0)
        audioHeader.writeTo(sendBuffer, FrameHeader.HEADER_SIZE)
        System.arraycopy(pcmData, offset, sendBuffer, FrameHeader.HEADER_SIZE + AudioHeader.HEADER_SIZE, length)

        synchronized(writeLock) {
            try {
                out.write(sendBuffer)
                out.flush()
            } catch (e: Exception) {
                isConnected = false
            }
        }
    }

    fun sendPing() {
        if (!isConnected) return
        val out = outputStream ?: return
        val nowUs = SystemClock.elapsedRealtimeNanos() / 1000L

        val payload = ByteArray(8)
        ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN).putLong(nowUs)

        val frameHeader = FrameHeader(MessageType.PING, 8)
        val sendBuffer = ByteArray(FrameHeader.HEADER_SIZE + 8)
        frameHeader.writeTo(sendBuffer, 0)
        System.arraycopy(payload, 0, sendBuffer, FrameHeader.HEADER_SIZE, 8)

        synchronized(writeLock) {
            try {
                out.write(sendBuffer)
                out.flush()
            } catch (e: Exception) {
                isConnected = false
            }
        }
    }

    fun sendSoundboardAction(action: String, clipId: String? = null) {
        if (!isConnected) return
        val json = JSONObject().apply {
            put("action", action)
            if (!clipId.isNullOrBlank()) {
                put("clipId", clipId)
            }
        }
        try {
            writeJson(MessageType.SOUNDBOARD_ACTION, json.toString())
        } catch (_: Exception) {
            isConnected = false
        }
    }

    suspend fun runFrameReceiverLoop() = withContext(Dispatchers.IO) {
        val inStream = inputStream ?: return@withContext
        val localHeaderBuffer = ByteArray(FrameHeader.HEADER_SIZE)

        while (isConnected) {
            try {
                var headerBytesRead = 0
                while (headerBytesRead < FrameHeader.HEADER_SIZE) {
                    val count = inStream.read(localHeaderBuffer, headerBytesRead, FrameHeader.HEADER_SIZE - headerBytesRead)
                    if (count < 0) {
                        isConnected = false
                        return@withContext
                    }
                    headerBytesRead += count
                }

                val parseResult = FrameHeader.parse(localHeaderBuffer, 0)
                if (parseResult.isFailure) {
                    continue
                }
                val frameHeader = parseResult.getOrThrow()

                val payload = ByteArray(frameHeader.payloadLength)
                var payloadBytesRead = 0
                while (payloadBytesRead < frameHeader.payloadLength) {
                    val count = inStream.read(payload, payloadBytesRead, frameHeader.payloadLength - payloadBytesRead)
                    if (count < 0) {
                        isConnected = false
                        return@withContext
                    }
                    payloadBytesRead += count
                }

                when (frameHeader.type) {
                    MessageType.PONG -> {
                        if (payload.size >= 8) {
                            val sentUs = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN).long
                            val nowUs = SystemClock.elapsedRealtimeNanos() / 1000L
                            val rtt = ((nowUs - sentUs) / 1000f).coerceAtLeast(0f)
                            lastRttMs = rtt
                        }
                    }
                    MessageType.SOUNDBOARD_SYNC -> {
                        try {
                            val jsonStr = String(payload, Charsets.UTF_8)
                            val jsonObj = JSONObject(jsonStr)
                            val isPlaying = jsonObj.optBoolean("isPlaying", false)
                            val currentPlayingClipName = if (jsonObj.has("currentPlayingClipName") && !jsonObj.isNull("currentPlayingClipName")) {
                                jsonObj.getString("currentPlayingClipName")
                            } else null

                            val clipsArray = jsonObj.optJSONArray("clips") ?: JSONArray()
                            val clipsList = ArrayList<SoundboardClip>(clipsArray.length())
                            for (i in 0 until clipsArray.length()) {
                                val cObj = clipsArray.getJSONObject(i)
                                clipsList.add(
                                    SoundboardClip(
                                        id = cObj.optString("id", ""),
                                        name = cObj.optString("name", ""),
                                        category = cObj.optString("category", "General"),
                                        durationText = cObj.optString("durationText", "0:00"),
                                        shortcutKey = cObj.optString("shortcutKey", ""),
                                        isPlaying = cObj.optBoolean("isPlaying", false)
                                    )
                                )
                            }

                            val syncData = SoundboardSyncData(
                                isPlaying = isPlaying,
                                currentPlayingClipName = currentPlayingClipName,
                                clips = clipsList
                            )
                            onSoundboardSyncReceived?.invoke(syncData)
                        } catch (e: Exception) {
                            e.printStackTrace()
                        }
                    }
                    else -> {
                        // Other frames
                    }
                }
            } catch (_: Exception) {
                if (isConnected) {
                    isConnected = false
                }
                break
            }
        }
    }

    private fun writeJson(type: MessageType, jsonString: String) {
        val out = outputStream ?: throw IllegalStateException("Socket output stream is null")
        val payloadBytes = jsonString.toByteArray(Charsets.UTF_8)
        val frameHeader = FrameHeader(type, payloadBytes.size)

        val buffer = ByteArray(FrameHeader.HEADER_SIZE + payloadBytes.size)
        frameHeader.writeTo(buffer, 0)
        System.arraycopy(payloadBytes, 0, buffer, FrameHeader.HEADER_SIZE, payloadBytes.size)

        synchronized(writeLock) {
            out.write(buffer)
            out.flush()
        }
    }

    private fun readFrame(): Pair<FrameHeader, ByteArray> {
        val inStream = inputStream ?: throw IllegalStateException("Socket input stream is null")

        // Read 8-byte header
        var headerBytesRead = 0
        while (headerBytesRead < FrameHeader.HEADER_SIZE) {
            val count = inStream.read(headerBuffer, headerBytesRead, FrameHeader.HEADER_SIZE - headerBytesRead)
            if (count < 0) throw IllegalStateException("Connection closed while reading frame header")
            headerBytesRead += count
        }

        val frameHeader = FrameHeader.parse(headerBuffer, 0).getOrThrow()

        // Read payload
        val payload = ByteArray(frameHeader.payloadLength)
        var payloadBytesRead = 0
        while (payloadBytesRead < frameHeader.payloadLength) {
            val count = inStream.read(payload, payloadBytesRead, frameHeader.payloadLength - payloadBytesRead)
            if (count < 0) throw IllegalStateException("Connection closed while reading payload")
            payloadBytesRead += count
        }

        return Pair(frameHeader, payload)
    }

    fun disconnect() {
        isConnected = false
        try { inputStream?.close() } catch (_: Exception) {}
        try { outputStream?.close() } catch (_: Exception) {}
        try { sslSocket?.close() } catch (_: Exception) {}
        try { rawSocket?.close() } catch (_: Exception) {}
        inputStream = null
        outputStream = null
        sslSocket = null
        rawSocket = null
    }
}

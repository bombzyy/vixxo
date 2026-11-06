package com.vixxo.mic.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Binder
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import com.vixxo.mic.MainActivity
import com.vixxo.mic.R
import com.vixxo.mic.audio.AudioCaptureEngine
import com.vixxo.mic.network.VixxoProtocolClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.util.UUID

enum class StreamState {
    IDLE,
    CONNECTING,
    STREAMING,
    RECONNECTING,
    ERROR
}

class MicrophoneStreamService : Service() {

    private val binder = LocalBinder()
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var streamJob: Job? = null
    private var pingJob: Job? = null

    private var wakeLock: PowerManager.WakeLock? = null
    private var audioCaptureEngine: AudioCaptureEngine? = null
    private var protocolClient: VixxoProtocolClient? = null
    private var isExplicitlyStopped = false

    private val _streamState = MutableStateFlow(StreamState.IDLE)
    val streamState: StateFlow<StreamState> = _streamState.asStateFlow()

    private val _micPeakLevel = MutableStateFlow(0f)
    val micPeakLevel: StateFlow<Float> = _micPeakLevel.asStateFlow()

    private val _framesSent = MutableStateFlow(0L)
    val framesSent: StateFlow<Long> = _framesSent.asStateFlow()

    private val _rttMs = MutableStateFlow(0f)
    val rttMs: StateFlow<Float> = _rttMs.asStateFlow()

    private val _isMuted = MutableStateFlow(false)
    val isMuted: StateFlow<Boolean> = _isMuted.asStateFlow()

    private val _serverName = MutableStateFlow("")
    val serverName: StateFlow<String> = _serverName.asStateFlow()

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    inner class LocalBinder : Binder() {
        fun getService(): MicrophoneStreamService = this@MicrophoneStreamService
    }

    override fun onBind(intent: Intent?): IBinder = binder

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()

        val powerManager = getSystemService(Context.POWER_SERVICE) as PowerManager
        wakeLock = powerManager.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Vixxo:StreamWakeLock").apply {
            setReferenceCounted(false)
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                val host = intent.getStringExtra(EXTRA_HOST) ?: "192.168.29.186"
                val port = intent.getIntExtra(EXTRA_PORT, 47800)
                val pin = intent.getStringExtra(EXTRA_PIN)
                val fp = intent.getStringExtra(EXTRA_FINGERPRINT)
                startStreaming(host, port, pin, fp)
            }
            ACTION_STOP -> {
                stopStreaming()
            }
            ACTION_TOGGLE_MUTE -> {
                toggleMute()
            }
        }
        return START_NOT_STICKY
    }

    fun startStreaming(host: String, port: Int, pairingPin: String? = null, pinnedFingerprint: String? = null) {
        if (_streamState.value == StreamState.STREAMING || _streamState.value == StreamState.CONNECTING) return

        isExplicitlyStopped = false
        _streamState.value = StreamState.CONNECTING
        _errorMessage.value = null
        wakeLock?.acquire(3 * 60 * 60 * 1000L) // 3 hours max

        startForegroundNotification()

        val secStore = com.vixxo.mic.security.SecurityStorage(this)
        val deviceId = secStore.deviceId

        streamJob = serviceScope.launch(Dispatchers.IO) {
            var attempt = 0
            val maxReconnectAttempts = 10

            while (isActive && !isExplicitlyStopped) {
                val savedCred = secStore.getDeviceCredential(host)
                val fp = pinnedFingerprint ?: secStore.getPinnedFingerprint(host)

                val client = VixxoProtocolClient(
                    host = host,
                    port = port,
                    deviceId = deviceId,
                    clientName = Build.MODEL,
                    pairingPinOrToken = pairingPin,
                    deviceCredential = savedCred,
                    pinnedFingerprint = fp,
                    onCredentialReceived = { newCred ->
                        secStore.saveDeviceCredential(host, newCred)
                        if (!fp.isNullOrBlank()) {
                            secStore.savePinnedFingerprint(host, fp)
                        }
                    }
                )
                protocolClient = client

                val connectResult = client.connect(timeoutMs = 5000)
                if (connectResult.isFailure) {
                    if (attempt == 0 && savedCred == null && pairingPin.isNullOrBlank()) {
                        // First attempt without credentials or PIN failed; abort immediately
                        _streamState.value = StreamState.ERROR
                        _errorMessage.value = connectResult.exceptionOrNull()?.message ?: "Connection failed"
                        break
                    }

                    attempt++
                    if (attempt > maxReconnectAttempts) {
                        _streamState.value = StreamState.ERROR
                        _errorMessage.value = "Reconnection failed after $maxReconnectAttempts attempts"
                        break
                    }

                    _streamState.value = StreamState.RECONNECTING
                    updateNotification()
                    val backoffMs = (1000L * (1L shl Math.min(attempt, 3))).coerceAtMost(8000L)
                    delay(backoffMs)
                    continue
                }

                attempt = 0
                val connectedServer = connectResult.getOrNull() ?: "Windows-PC"
                _serverName.value = connectedServer

                val engine = AudioCaptureEngine(sampleRate = 48000, frameSizeMs = 20)
                audioCaptureEngine = engine

                val startCapture = engine.start()
                if (startCapture.isFailure) {
                    client.disconnect()
                    _streamState.value = StreamState.ERROR
                    _errorMessage.value = startCapture.exceptionOrNull()?.message ?: "Audio capture failed"
                    break
                }

                _streamState.value = StreamState.STREAMING
                updateNotification()

                // Ping loop
                pingJob = serviceScope.launch(Dispatchers.IO) {
                    while (isActive && client.isConnected) {
                        delay(2000)
                        client.sendPing()
                    }
                }

                // Meter updater loop
                val meterJob = serviceScope.launch(Dispatchers.Default) {
                    while (isActive && engine == audioCaptureEngine) {
                        _micPeakLevel.value = engine.currentPeakLevel
                        delay(50)
                    }
                }

                var sentCount = 0L

                try {
                    while (isActive && !isExplicitlyStopped && client.isConnected) {
                        val chunk = engine.audioQueue.poll(50, java.util.concurrent.TimeUnit.MILLISECONDS)
                        if (chunk != null) {
                            client.sendAudioFrame(
                                seq = chunk.sequenceNumber,
                                timestampUs = chunk.timestampUs,
                                pcmData = chunk.data,
                                offset = 0,
                                length = chunk.length,
                                isMuted = engine.isMuted
                            )
                            sentCount++
                            if (sentCount % 25 == 0L) {
                                _framesSent.value = sentCount
                            }
                        }
                    }
                } catch (_: Exception) {
                } finally {
                    meterJob.cancel()
                    pingJob?.cancel()
                    engine.stop()
                    client.disconnect()
                    _micPeakLevel.value = 0f
                }

                if (isExplicitlyStopped) {
                    break
                } else {
                    _streamState.value = StreamState.RECONNECTING
                    updateNotification()
                    delay(1000)
                }
            }

            _streamState.value = StreamState.IDLE
            stopForegroundNotification()
        }
    }

    fun stopStreaming() {
        isExplicitlyStopped = true
        streamJob?.cancel()
        streamJob = null
        pingJob?.cancel()
        pingJob = null

        audioCaptureEngine?.stop()
        audioCaptureEngine = null

        protocolClient?.disconnect()
        protocolClient = null

        _streamState.value = StreamState.IDLE
        _micPeakLevel.value = 0f

        if (wakeLock?.isHeld == true) {
            wakeLock?.release()
        }

        stopForegroundNotification()
        stopSelf()
    }

    fun toggleMute() {
        val current = _isMuted.value
        val newMute = !current
        _isMuted.value = newMute
        audioCaptureEngine?.isMuted = newMute
        updateNotification()
    }

    fun setGain(gainLinear: Float) {
        audioCaptureEngine?.softwareGain = gainLinear
    }

    private fun startForegroundNotification() {
        val notification = buildNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun updateNotification() {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.notify(NOTIFICATION_ID, buildNotification())
    }

    private fun stopForegroundNotification() {
        stopForeground(STOP_FOREGROUND_REMOVE)
    }

    private fun buildNotification(): Notification {
        val openAppIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP
        }
        val openAppPendingIntent = PendingIntent.getActivity(
            this, 0, openAppIntent, PendingIntent.FLAG_IMMUTABLE
        )

        val stopIntent = Intent(this, MicrophoneStreamService::class.java).apply {
            action = ACTION_STOP
        }
        val stopPendingIntent = PendingIntent.getService(
            this, 1, stopIntent, PendingIntent.FLAG_IMMUTABLE
        )

        val muteIntent = Intent(this, MicrophoneStreamService::class.java).apply {
            action = ACTION_TOGGLE_MUTE
        }
        val mutePendingIntent = PendingIntent.getService(
            this, 2, muteIntent, PendingIntent.FLAG_IMMUTABLE
        )

        val muteTitle = if (_isMuted.value) getString(R.string.action_unmute) else getString(R.string.action_mute)

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.notification_title_streaming))
            .setContentText(if (_isMuted.value) "Microphone is MUTED" else getString(R.string.notification_text_streaming))
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setContentIntent(openAppPendingIntent)
            .setOngoing(true)
            .addAction(android.R.drawable.ic_menu_close_clear_cancel, getString(R.string.action_stop), stopPendingIntent)
            .addAction(android.R.drawable.ic_lock_silent_mode, muteTitle, mutePendingIntent)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.notification_channel_name),
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = getString(R.string.notification_channel_description)
            setShowBadge(false)
        }
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.createNotificationChannel(channel)
    }

    override fun onDestroy() {
        super.onDestroy()
        stopStreaming()
        serviceScope.cancel()
    }

    companion object {
        const val CHANNEL_ID = "vixxo_microphone_stream"
        const val NOTIFICATION_ID = 47800

        const val ACTION_START = "com.vixxo.mic.action.START"
        const val ACTION_STOP = "com.vixxo.mic.action.STOP"
        const val ACTION_TOGGLE_MUTE = "com.vixxo.mic.action.TOGGLE_MUTE"

        const val EXTRA_HOST = "extra_host"
        const val EXTRA_PORT = "extra_port"
        const val EXTRA_PIN = "extra_pin"
        const val EXTRA_FINGERPRINT = "extra_fingerprint"
    }
}

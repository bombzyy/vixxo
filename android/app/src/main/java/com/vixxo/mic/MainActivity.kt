package com.vixxo.mic

import android.Manifest
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.vixxo.mic.audio.AudioCaptureEngine
import com.vixxo.mic.discovery.DiscoveredServer
import com.vixxo.mic.discovery.UdpDiscoveryClient
import com.vixxo.mic.network.SoundboardClip
import com.vixxo.mic.service.MicrophoneStreamService
import com.vixxo.mic.service.StreamState
import com.vixxo.mic.ui.StreamScreen
import com.vixxo.mic.ui.VixxoTheme
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    private var streamService: MicrophoneStreamService? = null
    private var isBound = false

    // Local microphone test engine (runs without connecting to PC)
    private var localTestEngine: AudioCaptureEngine? = null
    private var localTestJob: Job? = null
    private val _isLocalTesting = MutableStateFlow(false)

    // Activity state flows synced from service
    private val _streamState = MutableStateFlow(StreamState.IDLE)
    private val _micPeakLevel = MutableStateFlow(0f)
    private val _framesSent = MutableStateFlow(0L)
    private val _serverName = MutableStateFlow("")
    private val _isMuted = MutableStateFlow(false)
    private val _errorMessage = MutableStateFlow<String?>(null)

    // Remote Soundboard State
    private val _soundboardClips = MutableStateFlow<List<SoundboardClip>>(emptyList())
    private val _isSoundboardPlaying = MutableStateFlow(false)
    private val _currentPlayingClipName = MutableStateFlow<String?>(null)

    // QR Scanning & LAN Discovery State
    private val _scannedQrPayload = mutableStateOf<String?>(null)
    private val _discoveredServers = MutableStateFlow<List<DiscoveredServer>>(emptyList())
    private val _isDiscovering = MutableStateFlow(false)

    private val qrScanLauncher = registerForActivityResult(ScanContract()) { result ->
        if (result.contents != null) {
            _scannedQrPayload.value = result.contents
            Toast.makeText(this, "QR Code scanned successfully!", Toast.LENGTH_SHORT).show()
        }
    }

    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
            val binder = service as? MicrophoneStreamService.LocalBinder
            val svc = binder?.getService()
            streamService = svc
            isBound = true

            if (svc != null) {
                lifecycleScope.launch {
                    svc.streamState.collect { _streamState.value = it }
                }
                lifecycleScope.launch {
                    svc.micPeakLevel.collect {
                        if (!_isLocalTesting.value) {
                            _micPeakLevel.value = it
                        }
                    }
                }
                lifecycleScope.launch {
                    svc.framesSent.collect { _framesSent.value = it }
                }
                lifecycleScope.launch {
                    svc.serverName.collect { _serverName.value = it }
                }
                lifecycleScope.launch {
                    svc.isMuted.collect { _isMuted.value = it }
                }
                lifecycleScope.launch {
                    svc.errorMessage.collect { _errorMessage.value = it }
                }
                lifecycleScope.launch {
                    svc.soundboardClips.collect { _soundboardClips.value = it }
                }
                lifecycleScope.launch {
                    svc.isSoundboardPlaying.collect { _isSoundboardPlaying.value = it }
                }
                lifecycleScope.launch {
                    svc.currentPlayingClipName.collect { _currentPlayingClipName.value = it }
                }
            }
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            streamService = null
            isBound = false
            _streamState.value = StreamState.IDLE
            _soundboardClips.value = emptyList()
            _isSoundboardPlaying.value = false
            _currentPlayingClipName.value = null
        }
    }

    private var pendingStartAction: (() -> Unit)? = null

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val recordAudioGranted = permissions[Manifest.permission.RECORD_AUDIO] == true
        if (recordAudioGranted) {
            pendingStartAction?.invoke()
            pendingStartAction = null
        } else {
            Toast.makeText(this, getString(R.string.perm_mic_required), Toast.LENGTH_LONG).show()
            pendingStartAction = null
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Bind to background service
        val serviceIntent = Intent(this, MicrophoneStreamService::class.java)
        bindService(serviceIntent, serviceConnection, Context.BIND_AUTO_CREATE)

        val prefs = getSharedPreferences("vixxo_prefs", Context.MODE_PRIVATE)
        val initialHost = prefs.getString("server_host", "192.168.29.186") ?: "192.168.29.186"
        val initialPort = prefs.getInt("server_port", 47800)
        val initialGainDb = prefs.getFloat("gain_db", 0f)

        setContent {
            VixxoTheme {
                val streamStateVal by _streamState.collectAsState()
                val micPeakVal by _micPeakLevel.collectAsState()
                val framesVal by _framesSent.collectAsState()
                val serverNameVal by _serverName.collectAsState()
                val isMutedVal by _isMuted.collectAsState()
                val errorVal by _errorMessage.collectAsState()
                val isLocalTestingVal by _isLocalTesting.collectAsState()

                val discoveredList by _discoveredServers.collectAsState()
                val isDiscoveringVal by _isDiscovering.collectAsState()

                val soundboardClipsVal by _soundboardClips.collectAsState()
                val isSoundboardPlayingVal by _isSoundboardPlaying.collectAsState()
                val currentPlayingClipNameVal by _currentPlayingClipName.collectAsState()

                StreamScreen(
                    streamState = streamStateVal,
                    micLevel = micPeakVal,
                    framesSent = framesVal,
                    serverName = serverNameVal,
                    isMuted = isMutedVal,
                    errorMessage = errorVal,
                    initialHost = initialHost,
                    initialPort = initialPort,
                    initialGainDb = initialGainDb,
                    onStartStream = { host, port, pin, fingerprint ->
                        prefs.edit().putString("server_host", host).putInt("server_port", port).apply()
                        checkAndRun(Manifest.permission.RECORD_AUDIO) {
                            startStreamService(host, port, pin, fingerprint)
                        }
                    },
                    onStopStream = {
                        streamService?.stopStreaming()
                    },
                    onToggleMute = {
                        streamService?.toggleMute()
                    },
                    onSetGain = { gainLinear ->
                        streamService?.setGain(gainLinear)
                        localTestEngine?.softwareGain = gainLinear
                        val gainDb = if (gainLinear > 0.001f) (20.0 * Math.log10(gainLinear.toDouble())).toFloat() else 0f
                        prefs.edit().putFloat("gain_db", gainDb).apply()
                    },
                    isLocalTesting = isLocalTestingVal,
                    onToggleLocalTest = {
                        toggleLocalMicTest()
                    },
                    onScanQrClick = {
                        checkAndRun(Manifest.permission.CAMERA) {
                            val options = ScanOptions().apply {
                                setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                                setPrompt("Scan Vixxo QR Code on your PC")
                                setCameraId(0)
                                setBeepEnabled(true)
                                setOrientationLocked(false)
                            }
                            qrScanLauncher.launch(options)
                        }
                    },
                    discoveredServers = discoveredList,
                    isDiscovering = isDiscoveringVal,
                    onDiscoverClick = {
                        triggerDiscovery()
                    },
                    scannedQrPayload = _scannedQrPayload.value,
                    soundboardClips = soundboardClipsVal,
                    isSoundboardPlaying = isSoundboardPlayingVal,
                    currentPlayingClipName = currentPlayingClipNameVal,
                    onPlaySoundboardClip = { clipId ->
                        streamService?.playSoundboardClip(clipId)
                    },
                    onStopSoundboardPlayback = {
                        streamService?.stopSoundboardPlayback()
                    },
                    onRequestSoundboardSync = {
                        streamService?.requestSoundboardSync()
                    }
                )
            }
        }
    }

    private fun triggerDiscovery() {
        lifecycleScope.launch {
            _isDiscovering.value = true
            val client = UdpDiscoveryClient(this@MainActivity)
            val servers = client.discoverServers(Build.MODEL)
            _discoveredServers.value = servers
            _isDiscovering.value = false
        }
    }

    private fun checkAndRun(permission: String, action: () -> Unit) {
        val permissionsToRequest = mutableListOf<String>()

        if (ContextCompat.checkSelfPermission(this, permission) != PackageManager.PERMISSION_GRANTED) {
            permissionsToRequest.add(permission)
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                permissionsToRequest.add(Manifest.permission.POST_NOTIFICATIONS)
            }
        }

        if (permissionsToRequest.isNotEmpty()) {
            pendingStartAction = action
            permissionLauncher.launch(permissionsToRequest.toTypedArray())
        } else {
            action.invoke()
        }
    }

    private fun startStreamService(host: String, port: Int, pin: String?, fingerprint: String?) {
        stopLocalMicTest()

        val intent = Intent(this, MicrophoneStreamService::class.java).apply {
            action = MicrophoneStreamService.ACTION_START
            putExtra(MicrophoneStreamService.EXTRA_HOST, host)
            putExtra(MicrophoneStreamService.EXTRA_PORT, port)
            if (!pin.isNullOrBlank()) putExtra(MicrophoneStreamService.EXTRA_PIN, pin)
            if (!fingerprint.isNullOrBlank()) putExtra(MicrophoneStreamService.EXTRA_FINGERPRINT, fingerprint)
        }
        ContextCompat.startForegroundService(this, intent)

        // If already bound, trigger direct start
        streamService?.startStreaming(host, port, pin, fingerprint)
    }

    private fun toggleLocalMicTest() {
        if (_isLocalTesting.value) {
            stopLocalMicTest()
        } else {
            checkAndRun(Manifest.permission.RECORD_AUDIO) {
                startLocalMicTest()
            }
        }
    }

    private fun startLocalMicTest() {
        if (_isLocalTesting.value) return

        val engine = AudioCaptureEngine(sampleRate = 48000, frameSizeMs = 20)
        val startResult = engine.start()
        if (startResult.isFailure) {
            Toast.makeText(this, "Failed to start microphone: ${startResult.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
            return
        }

        localTestEngine = engine
        _isLocalTesting.value = true

        localTestJob = lifecycleScope.launch {
            while (isActive && _isLocalTesting.value) {
                _micPeakLevel.value = engine.currentPeakLevel
                // Discard frames from queue so it doesn't back up
                engine.audioQueue.poll()
                delay(40)
            }
        }
    }

    private fun stopLocalMicTest() {
        localTestJob?.cancel()
        localTestJob = null
        localTestEngine?.stop()
        localTestEngine = null
        _isLocalTesting.value = false
        if (_streamState.value == StreamState.IDLE) {
            _micPeakLevel.value = 0f
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        stopLocalMicTest()
        if (isBound) {
            unbindService(serviceConnection)
            isBound = false
        }
    }
}

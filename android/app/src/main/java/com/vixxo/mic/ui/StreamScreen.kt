package com.vixxo.mic.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MicOff
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material.icons.filled.Usb
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.vixxo.mic.R
import com.vixxo.mic.discovery.DiscoveredServer
import com.vixxo.mic.network.SoundboardClip
import com.vixxo.mic.service.StreamState
import kotlin.math.sin

@Composable
fun StreamScreen(
    streamState: StreamState,
    micLevel: Float,
    framesSent: Long,
    serverName: String,
    isMuted: Boolean,
    errorMessage: String?,
    initialHost: String = "192.168.29.186",
    initialPort: Int = 47800,
    initialGainDb: Float = 0f,
    onStartStream: (String, Int, String?, String?) -> Unit,
    onStopStream: () -> Unit,
    onToggleMute: () -> Unit,
    onSetGain: (Float) -> Unit,
    isLocalTesting: Boolean,
    onToggleLocalTest: () -> Unit,
    onScanQrClick: () -> Unit,
    discoveredServers: List<DiscoveredServer>,
    isDiscovering: Boolean,
    onDiscoverClick: () -> Unit,
    scannedQrPayload: String?,
    soundboardClips: List<SoundboardClip> = emptyList(),
    isSoundboardPlaying: Boolean = false,
    currentPlayingClipName: String? = null,
    onPlaySoundboardClip: (String) -> Unit = {},
    onStopSoundboardPlayback: () -> Unit = {},
    onRequestSoundboardSync: () -> Unit = {}
) {
    var hostText by remember { mutableStateOf(initialHost) }
    var portText by remember { mutableStateOf(initialPort.toString()) }
    var pinText by remember { mutableStateOf("") }
    var pinnedFingerprint by remember { mutableStateOf<String?>(null) }
    var gainDb by remember { mutableFloatStateOf(initialGainDb) }
    var showDiscoveryDialog by remember { mutableStateOf(false) }
    var selectedTab by remember { mutableIntStateOf(0) } // 0 = Microphone, 1 = Soundboard
    var selectedCategory by remember { mutableStateOf("All") }

    // Auto-request soundboard sync whenever soundboard tab is selected or streaming connects
    LaunchedEffect(selectedTab, streamState) {
        if (selectedTab == 1 && streamState == StreamState.STREAMING) {
            onRequestSoundboardSync()
        }
    }

    // When QR code is scanned, parse JSON payload
    LaunchedEffect(scannedQrPayload) {
        if (!scannedQrPayload.isNullOrBlank()) {
            try {
                val json = org.json.JSONObject(scannedQrPayload)
                hostText = json.optString("host", hostText)
                portText = json.optInt("port", portText.toIntOrNull() ?: 47800).toString()
                pinText = json.optString("pin", json.optString("token", ""))
                pinnedFingerprint = if (json.has("fp")) json.getString("fp") else null
            } catch (_: Exception) {}
        }
    }

    val statusColor by animateColorAsState(
        targetValue = when (streamState) {
            StreamState.IDLE -> VixxoTextSecondary
            StreamState.CONNECTING -> VixxoWarning
            StreamState.STREAMING -> VixxoSuccess
            StreamState.RECONNECTING -> VixxoWarning
            StreamState.ERROR -> VixxoError
        },
        label = "statusColor"
    )

    val statusText = when (streamState) {
        StreamState.IDLE -> stringResource(R.string.stream_status_idle)
        StreamState.CONNECTING -> stringResource(R.string.stream_status_connecting)
        StreamState.STREAMING -> stringResource(R.string.stream_status_streaming)
        StreamState.RECONNECTING -> stringResource(R.string.stream_status_reconnecting)
        StreamState.ERROR -> stringResource(R.string.stream_status_error)
    }

    val isStreaming = streamState == StreamState.STREAMING
    val isConnecting = streamState == StreamState.CONNECTING
    val isUsbMode = hostText == "127.0.0.1"

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(VixxoBackground)
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        // =========================================================
        // 1. APP HEADER & STATUS PILL
        // =========================================================
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = "VIXXO",
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Black,
                    color = VixxoAccent,
                    letterSpacing = 1.5.sp
                )
                Spacer(modifier = Modifier.width(8.dp))
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(6.dp))
                        .background(VixxoSurfaceElevated)
                        .border(1.dp, VixxoBorder, RoundedCornerShape(6.dp))
                        .padding(horizontal = 6.dp, vertical = 2.dp)
                ) {
                    Text(
                        text = "STUDIO",
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold,
                        color = VixxoTextSecondary
                    )
                }
            }

            // Dynamic State Badge with glowing dot
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(20.dp))
                    .background(statusColor.copy(alpha = 0.15f))
                    .border(1.dp, statusColor.copy(alpha = 0.4f), RoundedCornerShape(20.dp))
                    .padding(horizontal = 12.dp, vertical = 6.dp)
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        modifier = Modifier
                            .size(8.dp)
                            .clip(CircleShape)
                            .background(statusColor)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = statusText.uppercase(),
                        color = statusColor,
                        fontSize = 11.sp,
                        fontWeight = FontWeight.Black,
                        letterSpacing = 0.5.sp
                    )
                }
            }
        }

        // =========================================================
        // NAVIGATION SWITCHER: [🎤 Microphone] | [🎵 Soundboard]
        // =========================================================
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(12.dp))
                .background(VixxoSurface)
                .border(1.dp, VixxoBorder, RoundedCornerShape(12.dp))
                .padding(4.dp),
            horizontalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            // Tab 0: Microphone
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(8.dp))
                    .background(if (selectedTab == 0) VixxoSurfaceElevated else Color.Transparent)
                    .border(
                        if (selectedTab == 0) 1.dp else 0.dp,
                        if (selectedTab == 0) VixxoBorderLight else Color.Transparent,
                        RoundedCornerShape(8.dp)
                    )
                    .clickable { selectedTab = 0 }
                    .padding(vertical = 10.dp),
                contentAlignment = Alignment.Center
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        imageVector = Icons.Default.Mic,
                        contentDescription = null,
                        tint = if (selectedTab == 0) VixxoAccent else VixxoTextSecondary,
                        modifier = Modifier.size(16.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = "Microphone",
                        fontSize = 13.sp,
                        fontWeight = if (selectedTab == 0) FontWeight.Bold else FontWeight.Medium,
                        color = if (selectedTab == 0) VixxoTextPrimary else VixxoTextSecondary
                    )
                }
            }

            // Tab 1: Soundboard
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(8.dp))
                    .background(if (selectedTab == 1) VixxoSurfaceElevated else Color.Transparent)
                    .border(
                        if (selectedTab == 1) 1.dp else 0.dp,
                        if (selectedTab == 1) VixxoBorderLight else Color.Transparent,
                        RoundedCornerShape(8.dp)
                    )
                    .clickable { selectedTab = 1 }
                    .padding(vertical = 10.dp),
                contentAlignment = Alignment.Center
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = "🎵",
                        fontSize = 13.sp
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = if (soundboardClips.isNotEmpty()) "Soundboard (${soundboardClips.size})" else "Soundboard",
                        fontSize = 13.sp,
                        fontWeight = if (selectedTab == 1) FontWeight.Bold else FontWeight.Medium,
                        color = if (selectedTab == 1) VixxoTextPrimary else VixxoTextSecondary
                    )
                    if (isSoundboardPlaying) {
                        Spacer(modifier = Modifier.width(6.dp))
                        Box(
                            modifier = Modifier
                                .size(6.dp)
                                .clip(CircleShape)
                                .background(VixxoSuccess)
                        )
                    }
                }
            }
        }

        if (selectedTab == 0) {
            // =========================================================
            // 2. HERO REAL-TIME AUDIO VISUALIZER CARD
            // =========================================================
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = VixxoSurface),
            shape = RoundedCornerShape(18.dp),
            border = androidx.compose.foundation.BorderStroke(1.dp, VixxoBorder)
        ) {
            Column(
                modifier = Modifier.padding(18.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = "🎙️",
                            fontSize = 14.sp
                        )
                        Spacer(modifier = Modifier.width(6.dp))
                        Text(
                            text = "Microphone Signal",
                            color = VixxoTextSecondary,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.SemiBold
                        )
                    }

                    Text(
                        text = when {
                            isMuted -> "MUTED"
                            isStreaming || isLocalTesting -> "${(micLevel * 100).toInt()}%"
                            else -> "STANDBY"
                        },
                        color = when {
                            isMuted -> VixxoError
                            micLevel > 0.85f -> VixxoError
                            isStreaming || isLocalTesting -> VixxoAccent
                            else -> VixxoTextMuted
                        },
                        fontSize = 13.sp,
                        fontFamily = FontFamily.Monospace,
                        fontWeight = FontWeight.Bold
                    )
                }

                // Studio Multi-Segment Equalizer Canvas
                val animatedLevel by animateFloatAsState(
                    targetValue = if (isMuted) 0f else micLevel,
                    animationSpec = spring(stiffness = Spring.StiffnessMediumLow),
                    label = "micLevelAnim"
                )

                StudioVisualizerCanvas(
                    level = animatedLevel,
                    isActive = isStreaming || isLocalTesting,
                    isMuted = isMuted,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(56.dp)
                )

                // Subtitle specs row
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = "48 kHz • 16-bit Mono PCM",
                        fontSize = 10.sp,
                        color = VixxoTextMuted
                    )
                    Text(
                        text = if (isUsbMode) "USB Sub-5ms Loopback" else "Wi-Fi UDP Beacon",
                        fontSize = 10.sp,
                        color = if (isUsbMode) VixxoAccent else VixxoTextMuted
                    )
                }
            }
        }

        // =========================================================
        // 3. TRANSPORT MODE QUICK SELECTOR (USB / WI-FI / QR)
        // =========================================================
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // Mode 1: USB (Ultra Low Latency)
            val usbSelected = isUsbMode
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(12.dp))
                    .background(if (usbSelected) VixxoAccent.copy(alpha = 0.15f) else VixxoSurface)
                    .border(
                        1.dp,
                        if (usbSelected) VixxoAccent else VixxoBorder,
                        RoundedCornerShape(12.dp)
                    )
                    .clickable(enabled = !isStreaming && !isConnecting) {
                        hostText = "127.0.0.1"
                        portText = "47800"
                    }
                    .padding(vertical = 12.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(
                        imageVector = Icons.Default.Usb,
                        contentDescription = "USB",
                        tint = if (usbSelected) VixxoAccent else VixxoTextSecondary,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "USB Mode",
                        fontSize = 11.sp,
                        fontWeight = if (usbSelected) FontWeight.Bold else FontWeight.Normal,
                        color = if (usbSelected) VixxoAccent else VixxoTextSecondary
                    )
                }
            }

            // Mode 2: Wi-Fi Discovery
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(12.dp))
                    .background(VixxoSurface)
                    .border(1.dp, VixxoBorder, RoundedCornerShape(12.dp))
                    .clickable(enabled = !isStreaming && !isConnecting) {
                        onDiscoverClick()
                        showDiscoveryDialog = true
                    }
                    .padding(vertical = 12.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(
                        imageVector = Icons.Default.Wifi,
                        contentDescription = "Wi-Fi",
                        tint = VixxoTextSecondary,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "Find PC",
                        fontSize = 11.sp,
                        color = VixxoTextSecondary
                    )
                }
            }

            // Mode 3: Scan QR
            Box(
                modifier = Modifier
                    .weight(1f)
                    .clip(RoundedCornerShape(12.dp))
                    .background(VixxoSurface)
                    .border(1.dp, VixxoBorder, RoundedCornerShape(12.dp))
                    .clickable(enabled = !isStreaming && !isConnecting) {
                        onScanQrClick()
                    }
                    .padding(vertical = 12.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(
                        imageVector = Icons.Default.QrCodeScanner,
                        contentDescription = "Scan QR",
                        tint = VixxoTextSecondary,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "Scan QR",
                        fontSize = 11.sp,
                        color = VixxoTextSecondary
                    )
                }
            }
        }

        // =========================================================
        // 4. LIVE TELEMETRY CARD (WHEN STREAMING)
        // =========================================================
        if (isStreaming) {
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = VixxoSurfaceElevated),
                shape = RoundedCornerShape(16.dp),
                border = androidx.compose.foundation.BorderStroke(1.dp, VixxoBorderLight)
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "Active Stream Telemetry",
                            color = VixxoAccent,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold
                        )
                        Text(
                            text = if (isUsbMode) "< 5 ms (USB)" else "LAN Wi-Fi",
                            color = VixxoSuccess,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Column {
                            Text("TARGET SERVER", fontSize = 10.sp, color = VixxoTextMuted)
                            Text(serverName.ifEmpty { "$hostText:$portText" }, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = VixxoTextPrimary)
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text("FRAMES SENT", fontSize = 10.sp, color = VixxoTextMuted)
                            Text("$framesSent (${framesSent * 20 / 1000}s)", fontSize = 13.sp, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = VixxoTextPrimary)
                        }
                    }
                }
            }
        }

        // =========================================================
        // 5. CONNECTION PARAMETERS CARD
        // =========================================================
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = VixxoSurface),
            shape = RoundedCornerShape(16.dp),
            border = androidx.compose.foundation.BorderStroke(1.dp, VixxoBorder)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(
                    text = "Connection Endpoint",
                    color = VixxoTextPrimary,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold
                )

                // IP and Port
                Row(modifier = Modifier.fillMaxWidth()) {
                    OutlinedTextField(
                        value = hostText,
                        onValueChange = { hostText = it },
                        label = { Text("PC IP Address", fontSize = 11.sp) },
                        modifier = Modifier.weight(0.7f),
                        enabled = !isStreaming && !isConnecting,
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = VixxoAccent,
                            unfocusedBorderColor = VixxoBorder,
                            focusedTextColor = VixxoTextPrimary,
                            unfocusedTextColor = VixxoTextPrimary,
                            focusedContainerColor = VixxoSurfaceSubtle,
                            unfocusedContainerColor = VixxoSurfaceSubtle
                        ),
                        shape = RoundedCornerShape(10.dp)
                    )

                    Spacer(modifier = Modifier.width(8.dp))

                    OutlinedTextField(
                        value = portText,
                        onValueChange = { portText = it },
                        label = { Text("Port", fontSize = 11.sp) },
                        modifier = Modifier.weight(0.3f),
                        enabled = !isStreaming && !isConnecting,
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = VixxoAccent,
                            unfocusedBorderColor = VixxoBorder,
                            focusedTextColor = VixxoTextPrimary,
                            unfocusedTextColor = VixxoTextPrimary,
                            focusedContainerColor = VixxoSurfaceSubtle,
                            unfocusedContainerColor = VixxoSurfaceSubtle
                        ),
                        shape = RoundedCornerShape(10.dp)
                    )
                }

                // PIN Field
                OutlinedTextField(
                    value = pinText,
                    onValueChange = { pinText = it },
                    label = { Text("6-Digit PIN (Optional if already paired)", fontSize = 11.sp) },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = !isStreaming && !isConnecting,
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = VixxoAccent,
                        unfocusedBorderColor = VixxoBorder,
                        focusedTextColor = VixxoTextPrimary,
                        unfocusedTextColor = VixxoTextPrimary,
                        focusedContainerColor = VixxoSurfaceSubtle,
                        unfocusedContainerColor = VixxoSurfaceSubtle
                    ),
                    shape = RoundedCornerShape(10.dp)
                )

                if (!errorMessage.isNullOrBlank()) {
                    Text(
                        text = "⚠️ $errorMessage",
                        color = VixxoError,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                }
            }
        }

        // =========================================================
        // 6. SOFTWARE GAIN SLIDER CARD
        // =========================================================
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = VixxoSurface),
            shape = RoundedCornerShape(16.dp),
            border = androidx.compose.foundation.BorderStroke(1.dp, VixxoBorder)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "Microphone Gain",
                        color = VixxoTextSecondary,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                    Text(
                        text = "+${gainDb.toInt()} dB",
                        color = VixxoAccent,
                        fontSize = 13.sp,
                        fontFamily = FontFamily.Monospace,
                        fontWeight = FontWeight.Bold
                    )
                }

                Slider(
                    value = gainDb,
                    onValueChange = {
                        gainDb = it
                        val linear = Math.pow(10.0, it / 20.0).toFloat()
                        onSetGain(linear)
                    },
                    valueRange = 0f..18f,
                    steps = 5,
                    colors = SliderDefaults.colors(
                        thumbColor = VixxoAccent,
                        activeTrackColor = VixxoAccent,
                        inactiveTrackColor = VixxoSurfaceSubtle
                    )
                )
            }
        }

        Spacer(modifier = Modifier.height(8.dp))

        // =========================================================
        // 7. HERO ACTION ROW (MUTE + START/STOP STREAM)
        // =========================================================
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            // Mute Button (Tactile 35% width)
            OutlinedButton(
                onClick = onToggleMute,
                modifier = Modifier
                    .weight(0.35f)
                    .height(58.dp),
                shape = RoundedCornerShape(14.dp),
                enabled = isStreaming,
                colors = ButtonDefaults.outlinedButtonColors(
                    contentColor = if (isMuted) VixxoError else VixxoTextPrimary,
                    containerColor = if (isMuted) VixxoError.copy(alpha = 0.15f) else Color.Transparent
                ),
                border = androidx.compose.foundation.BorderStroke(
                    1.dp,
                    if (isMuted) VixxoError else VixxoBorder
                )
            ) {
                Icon(
                    imageVector = if (isMuted) Icons.Default.MicOff else Icons.Default.Mic,
                    contentDescription = "Mute",
                    tint = if (isMuted) VixxoError else VixxoTextPrimary
                )
                Spacer(modifier = Modifier.width(6.dp))
                Text(
                    text = if (isMuted) "Unmute" else "Mute",
                    fontWeight = FontWeight.Bold,
                    fontSize = 13.sp
                )
            }

            // Start / Stop Main Stream Button (Glow Hero 65% width)
            Button(
                onClick = {
                    if (isStreaming || isConnecting) {
                        onStopStream()
                    } else {
                        val portInt = portText.toIntOrNull() ?: 47800
                        val pin = pinText.replace(" ", "").trim().ifEmpty { null }
                        onStartStream(hostText.trim(), portInt, pin, pinnedFingerprint)
                    }
                },
                modifier = Modifier
                    .weight(0.65f)
                    .height(58.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isStreaming) VixxoError else VixxoAccent,
                    contentColor = if (isStreaming) Color.White else Color(0xFF060D1A)
                )
            ) {
                Icon(
                    imageVector = if (isStreaming) Icons.Default.Stop else Icons.Default.PlayArrow,
                    contentDescription = null
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = if (isStreaming) "STOP STREAMING" else "START STREAMING",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Black,
                    letterSpacing = 0.5.sp
                )
            }
        }

        // =========================================================
        // 8. LOCAL MIC TEST BUTTON
        // =========================================================
        OutlinedButton(
            onClick = onToggleLocalTest,
            modifier = Modifier
                .fillMaxWidth()
                .height(44.dp),
            shape = RoundedCornerShape(12.dp),
            enabled = !isStreaming && !isConnecting,
            colors = ButtonDefaults.outlinedButtonColors(
                contentColor = if (isLocalTesting) VixxoWarning else VixxoTextMuted
            ),
            border = androidx.compose.foundation.BorderStroke(
                1.dp,
                if (isLocalTesting) VixxoWarning else VixxoBorder
            )
        ) {
            Text(
                text = if (isLocalTesting) "■ Stop Mic Test" else "▶ Test Microphone Level (Offline)",
                fontSize = 12.sp,
                fontWeight = FontWeight.SemiBold
            )
        }
        } else {
            // =========================================================
            // SOUNDBOARD STREAM DECK MOBILE TAB
            // =========================================================
            if (!isStreaming) {
                // Not connected to PC
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = VixxoSurface),
                    shape = RoundedCornerShape(18.dp),
                    border = BorderStroke(1.dp, VixxoBorder)
                ) {
                    Column(
                        modifier = Modifier.padding(24.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(16.dp)
                    ) {
                        Box(
                            modifier = Modifier
                                .size(64.dp)
                                .clip(CircleShape)
                                .background(VixxoSurfaceElevated)
                                .border(1.dp, VixxoBorderLight, CircleShape),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(text = "📡", fontSize = 28.sp)
                        }

                        Text(
                            text = "Connect to PC First",
                            fontSize = 18.sp,
                            fontWeight = FontWeight.Bold,
                            color = VixxoTextPrimary
                        )

                        Text(
                            text = "Soundboard clips configured in Vixxo Studio on your Windows PC sync here automatically in real time. Connect to your PC over Wi-Fi or USB to use your phone as a Stream Deck.",
                            fontSize = 13.sp,
                            color = VixxoTextSecondary,
                            textAlign = TextAlign.Center,
                            lineHeight = 18.sp
                        )

                        Button(
                            onClick = { selectedTab = 0 },
                            colors = ButtonDefaults.buttonColors(containerColor = VixxoAccent),
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(48.dp)
                        ) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(
                                    imageVector = Icons.Default.PlayArrow,
                                    contentDescription = null,
                                    tint = Color(0xFF060D1A)
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                Text(
                                    text = "Go to Connect Screen",
                                    color = Color(0xFF060D1A),
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 14.sp
                                )
                            }
                        }
                    }
                }
            } else {
                // Connected & streaming: Live Soundboard Stream Deck
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(
                        containerColor = if (isSoundboardPlaying) VixxoAccentSubtle.copy(alpha = 0.5f) else VixxoSurface
                    ),
                    shape = RoundedCornerShape(18.dp),
                    border = BorderStroke(
                        1.dp,
                        if (isSoundboardPlaying) VixxoAccent.copy(alpha = 0.6f) else VixxoBorder
                    )
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(16.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Box(
                                    modifier = Modifier
                                        .size(8.dp)
                                        .clip(CircleShape)
                                        .background(if (isSoundboardPlaying) VixxoSuccess else VixxoTextMuted)
                                )
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(
                                    text = if (isSoundboardPlaying) "NOW PLAYING ON PC" else "SOUNDBOARD READY",
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = if (isSoundboardPlaying) VixxoSuccess else VixxoTextSecondary,
                                    letterSpacing = 0.5.sp
                                )
                            }
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = if (isSoundboardPlaying && !currentPlayingClipName.isNullOrBlank()) {
                                    currentPlayingClipName
                                } else {
                                    "Tap any sound pad below"
                                },
                                fontSize = 15.sp,
                                fontWeight = FontWeight.Black,
                                color = if (isSoundboardPlaying) VixxoAccent else VixxoTextPrimary,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }

                        Spacer(modifier = Modifier.width(8.dp))

                        // Quick Sync Button
                        OutlinedButton(
                            onClick = onRequestSoundboardSync,
                            shape = RoundedCornerShape(10.dp),
                            border = BorderStroke(1.dp, VixxoBorderLight),
                            colors = ButtonDefaults.outlinedButtonColors(contentColor = VixxoAccent),
                            modifier = Modifier.height(40.dp)
                        ) {
                            Icon(
                                imageVector = Icons.Default.Refresh,
                                contentDescription = "Sync",
                                tint = VixxoAccent,
                                modifier = Modifier.size(16.dp)
                            )
                        }

                        Spacer(modifier = Modifier.width(6.dp))

                        // Stop All Button
                        Button(
                            onClick = onStopSoundboardPlayback,
                            colors = ButtonDefaults.buttonColors(
                                containerColor = VixxoError.copy(alpha = 0.2f),
                                contentColor = VixxoError
                            ),
                            shape = RoundedCornerShape(10.dp),
                            border = BorderStroke(1.dp, VixxoError),
                            modifier = Modifier.height(40.dp)
                        ) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(
                                    imageVector = Icons.Default.Stop,
                                    contentDescription = "Stop",
                                    tint = VixxoError,
                                    modifier = Modifier.size(16.dp)
                                )
                                Spacer(modifier = Modifier.width(4.dp))
                                Text(
                                    text = "STOP",
                                    fontWeight = FontWeight.Black,
                                    fontSize = 12.sp,
                                    color = VixxoError
                                )
                            }
                        }
                    }
                }

                // Category Filter Chips
                val categories = remember(soundboardClips) {
                    val rawCats = soundboardClips.map { it.category.trim() }.filter { it.isNotBlank() }.distinct()
                    listOf("All") + rawCats
                }

                if (categories.size > 2) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .horizontalScroll(rememberScrollState()),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        categories.forEach { cat ->
                            val isSelected = cat == selectedCategory
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(20.dp))
                                    .background(if (isSelected) VixxoAccentSubtle else VixxoSurface)
                                    .border(
                                        1.dp,
                                        if (isSelected) VixxoAccent else VixxoBorder,
                                        RoundedCornerShape(20.dp)
                                    )
                                    .clickable { selectedCategory = cat }
                                    .padding(horizontal = 14.dp, vertical = 6.dp)
                            ) {
                                Text(
                                    text = cat,
                                    fontSize = 12.sp,
                                    fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Medium,
                                    color = if (isSelected) VixxoAccent else VixxoTextSecondary
                                )
                            }
                        }
                    }
                }

                val filteredClips = remember(soundboardClips, selectedCategory) {
                    if (selectedCategory == "All") soundboardClips
                    else soundboardClips.filter { it.category.trim() == selectedCategory }
                }

                if (filteredClips.isEmpty()) {
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        colors = CardDefaults.cardColors(containerColor = VixxoSurface),
                        shape = RoundedCornerShape(16.dp),
                        border = BorderStroke(1.dp, VixxoBorder)
                    ) {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(32.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Text(text = "🎵", fontSize = 32.sp)
                            Text(
                                text = "No Sound Clips Found",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.Bold,
                                color = VixxoTextPrimary
                            )
                            Text(
                                text = "Open Vixxo on your PC, click 'Add Sound' in the Soundboard tab, and your sound clips will appear here instantly.",
                                fontSize = 12.sp,
                                color = VixxoTextSecondary,
                                textAlign = TextAlign.Center
                            )
                            Button(
                                onClick = onRequestSoundboardSync,
                                colors = ButtonDefaults.buttonColors(containerColor = VixxoAccent),
                                shape = RoundedCornerShape(10.dp),
                                modifier = Modifier.padding(top = 8.dp)
                            ) {
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    Icon(
                                        imageVector = Icons.Default.Refresh,
                                        contentDescription = null,
                                        tint = Color(0xFF060D1A),
                                        modifier = Modifier.size(16.dp)
                                    )
                                    Spacer(modifier = Modifier.width(6.dp))
                                    Text(
                                        text = "Sync Sounds from PC",
                                        color = Color(0xFF060D1A),
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 13.sp
                                    )
                                }
                            }
                        }
                    }
                } else {
                    // 2-column Sound Pad Grid
                    val chunkedRows = filteredClips.chunked(2)
                    chunkedRows.forEach { rowClips ->
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            rowClips.forEach { clip ->
                                Box(modifier = Modifier.weight(1f)) {
                                    SoundPadCard(
                                        clip = clip,
                                        onClick = { onPlaySoundboardClip(clip.id) }
                                    )
                                }
                            }
                            if (rowClips.size == 1) {
                                Spacer(modifier = Modifier.weight(1f))
                            }
                        }
                    }
                }
            }
        }
    }

    // =========================================================
    // 9. AUTO-DISCOVERY DIALOG
    // =========================================================
    if (showDiscoveryDialog) {
        AlertDialog(
            onDismissRequest = { showDiscoveryDialog = false },
            title = {
                Text(
                    "Select Vixxo PC",
                    color = VixxoTextPrimary,
                    fontWeight = FontWeight.Bold,
                    fontSize = 17.sp
                )
            },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (isDiscovering) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            CircularProgressIndicator(
                                color = VixxoAccent,
                                modifier = Modifier.size(20.dp),
                                strokeWidth = 2.dp
                            )
                            Spacer(modifier = Modifier.width(10.dp))
                            Text("Searching local Wi-Fi beacon...", color = VixxoTextSecondary, fontSize = 13.sp)
                        }
                    } else if (discoveredServers.isEmpty()) {
                        Text(
                            "No PCs found on Wi-Fi. Ensure Vixxo is running on your PC and both devices are on the same network (or switch to USB Mode).",
                            color = VixxoTextSecondary,
                            fontSize = 12.sp
                        )
                    } else {
                        LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            items(discoveredServers) { server ->
                                Card(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .clickable {
                                            hostText = server.host
                                            portText = server.tcpPort.toString()
                                            pinnedFingerprint = server.fingerprint.ifEmpty { null }
                                            showDiscoveryDialog = false
                                        },
                                    colors = CardDefaults.cardColors(containerColor = VixxoSurfaceSubtle),
                                    shape = RoundedCornerShape(10.dp),
                                    border = androidx.compose.foundation.BorderStroke(1.dp, VixxoBorder)
                                ) {
                                    Column(modifier = Modifier.padding(12.dp)) {
                                        Text(server.serverName, fontWeight = FontWeight.Bold, color = VixxoAccent, fontSize = 13.sp)
                                        Text("${server.host}:${server.tcpPort}", color = VixxoTextMuted, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
                                    }
                                }
                            }
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = onDiscoverClick) {
                    Text("Refresh", color = VixxoAccent, fontWeight = FontWeight.Bold)
                }
            },
            dismissButton = {
                TextButton(onClick = { showDiscoveryDialog = false }) {
                    Text("Close", color = VixxoTextSecondary)
                }
            },
            containerColor = VixxoSurface,
            shape = RoundedCornerShape(16.dp)
        )
    }
}

/**
 * Custom Studio Multi-Bar Equalizer Canvas
 */
@Composable
fun StudioVisualizerCanvas(
    level: Float,
    isActive: Boolean,
    isMuted: Boolean,
    modifier: Modifier = Modifier
) {
    Canvas(modifier = modifier) {
        val barCount = 28
        val spacing = 4.dp.toPx()
        val totalSpacing = spacing * (barCount - 1)
        val barWidth = (size.width - totalSpacing) / barCount
        val maxHeight = size.height

        for (i in 0 until barCount) {
            val barNormalizedIndex = i.toFloat() / barCount
            val curveFactor = 0.5f + 0.5f * sin(barNormalizedIndex * Math.PI).toFloat()

            val barHeight = if (!isActive || isMuted) {
                4.dp.toPx()
            } else {
                val calculated = maxHeight * level * curveFactor * (0.6f + 0.4f * sin(i * 1.5).toFloat().coerceAtLeast(0f))
                calculated.coerceIn(4.dp.toPx(), maxHeight)
            }

            val color = when {
                isMuted -> VixxoError.copy(alpha = 0.4f)
                !isActive -> VixxoBorder
                barNormalizedIndex > 0.85f -> VixxoError
                barNormalizedIndex > 0.65f -> VixxoWarning
                else -> VixxoAccent
            }

            val x = i * (barWidth + spacing)
            val y = (maxHeight - barHeight) / 2f

            drawRoundRect(
                color = color,
                topLeft = Offset(x, y),
                size = Size(barWidth, barHeight),
                cornerRadius = CornerRadius(2.dp.toPx(), 2.dp.toPx())
            )
        }
    }
}

/**
 * Tactical Stream Deck Sound Pad Card
 */
@Composable
fun SoundPadCard(
    clip: SoundboardClip,
    onClick: () -> Unit
) {
    val isPlaying = clip.isPlaying
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isPlaying) VixxoAccentSubtle else VixxoSurface
        ),
        border = BorderStroke(
            if (isPlaying) 1.5.dp else 1.dp,
            if (isPlaying) VixxoAccent else VixxoBorder
        )
    ) {
        Column(
            modifier = Modifier.padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Category pill
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(4.dp))
                        .background(VixxoSurfaceElevated)
                        .padding(horizontal = 5.dp, vertical = 2.dp)
                ) {
                    Text(
                        text = clip.category.ifBlank { "GENERAL" }.uppercase(),
                        fontSize = 9.sp,
                        fontWeight = FontWeight.Bold,
                        color = VixxoTextMuted,
                        maxLines = 1
                    )
                }

                // Duration text
                Text(
                    text = clip.durationText,
                    fontSize = 10.sp,
                    fontFamily = FontFamily.Monospace,
                    color = VixxoTextSecondary
                )
            }

            // Clip Name
            Text(
                text = clip.name,
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                color = if (isPlaying) VixxoAccent else VixxoTextPrimary,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                lineHeight = 16.sp,
                modifier = Modifier.height(34.dp)
            )

            // Bottom bar: Hotkey + Indicator
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                if (clip.shortcutKey.isNotBlank()) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(4.dp))
                            .background(VixxoSurfaceElevated)
                            .border(1.dp, VixxoBorderLight, RoundedCornerShape(4.dp))
                            .padding(horizontal = 4.dp, vertical = 1.dp)
                    ) {
                        Text(
                            text = clip.shortcutKey,
                            fontSize = 9.sp,
                            fontFamily = FontFamily.Monospace,
                            fontWeight = FontWeight.SemiBold,
                            color = VixxoAccentHover
                        )
                    }
                } else {
                    Spacer(modifier = Modifier.width(1.dp))
                }

                if (isPlaying) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(
                            modifier = Modifier
                                .size(6.dp)
                                .clip(CircleShape)
                                .background(VixxoSuccess)
                        )
                        Spacer(modifier = Modifier.width(4.dp))
                        Text(
                            text = "PLAYING",
                            fontSize = 9.sp,
                            fontWeight = FontWeight.Black,
                            color = VixxoSuccess
                        )
                    }
                } else {
                    Icon(
                        imageVector = Icons.Default.PlayArrow,
                        contentDescription = "Play",
                        tint = VixxoTextMuted,
                        modifier = Modifier.size(14.dp)
                    )
                }
            }
        }
    }
}

package com.vixxo.mic.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MicOff
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material.icons.filled.Usb
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.vixxo.mic.R
import com.vixxo.mic.discovery.DiscoveredServer
import com.vixxo.mic.service.StreamState

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
    scannedQrPayload: String?
) {
    var hostText by remember { mutableStateOf(initialHost) }
    var portText by remember { mutableStateOf(initialPort.toString()) }
    var pinText by remember { mutableStateOf("") }
    var pinnedFingerprint by remember { mutableStateOf<String?>(null) }
    var gainDb by remember { mutableFloatStateOf(initialGainDb) }
    var showDiscoveryDialog by remember { mutableStateOf(false) }

    // When QR code is scanned, parse JSON payload
    LaunchedEffect(scannedQrPayload) {
        if (!scannedQrPayload.isNullOrBlank()) {
            try {
                val json = org.json.JSONObject(scannedQrPayload)
                hostText = json.optString("host", hostText)
                portText = json.optInt("port", portText.toIntOrNull() ?: 47800).toString()
                pinText = json.optString("pin", json.optString("token", ""))
                pinnedFingerprint = json.optString("fp", null)
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

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(VixxoBackground)
            .padding(20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        // App Header
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(
                text = "Vixxo",
                fontSize = 28.sp,
                fontWeight = FontWeight.Bold,
                color = VixxoAccent
            )

            // Status Pill
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(12.dp))
                    .background(statusColor.copy(alpha = 0.2f))
                    .padding(horizontal = 12.dp, vertical = 6.dp)
            ) {
                Text(
                    text = statusText,
                    color = statusColor,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.SemiBold
                )
            }
        }

        // Connection Card
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = VixxoSurface),
            shape = RoundedCornerShape(16.dp)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(
                    text = "Windows PC Connection",
                    color = VixxoTextPrimary,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.Bold
                )

                // Quick Action Buttons: Scan QR, Discover LAN, & USB Mode
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    OutlinedButton(
                        onClick = onScanQrClick,
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(10.dp),
                        enabled = streamState == StreamState.IDLE
                    ) {
                        Icon(Icons.Default.QrCodeScanner, contentDescription = null, tint = VixxoAccent)
                        Spacer(modifier = Modifier.width(4.dp))
                        Text("QR", color = VixxoTextPrimary, fontSize = 12.sp)
                    }

                    OutlinedButton(
                        onClick = {
                            onDiscoverClick()
                            showDiscoveryDialog = true
                        },
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(10.dp),
                        enabled = streamState == StreamState.IDLE
                    ) {
                        Icon(Icons.Default.Search, contentDescription = null, tint = VixxoAccent)
                        Spacer(modifier = Modifier.width(4.dp))
                        Text("Wi-Fi", color = VixxoTextPrimary, fontSize = 12.sp)
                    }

                    OutlinedButton(
                        onClick = {
                            hostText = "127.0.0.1"
                            portText = "47800"
                        },
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(10.dp),
                        enabled = streamState == StreamState.IDLE,
                        colors = if (hostText == "127.0.0.1") ButtonDefaults.outlinedButtonColors(containerColor = VixxoAccent.copy(alpha = 0.15f)) else ButtonDefaults.outlinedButtonColors()
                    ) {
                        Icon(Icons.Default.Usb, contentDescription = null, tint = VixxoAccent)
                        Spacer(modifier = Modifier.width(4.dp))
                        Text("USB", color = VixxoTextPrimary, fontSize = 12.sp)
                    }
                }

                if (hostText == "127.0.0.1") {
                    Text("⚡ USB Mode selected (adb reverse 127.0.0.1)", color = VixxoAccent, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                }

                // IP and Port
                Row(modifier = Modifier.fillMaxWidth()) {
                    OutlinedTextField(
                        value = hostText,
                        onValueChange = { hostText = it },
                        label = { Text(stringResource(R.string.field_server_ip), fontSize = 12.sp) },
                        modifier = Modifier.weight(0.7f),
                        enabled = streamState == StreamState.IDLE,
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = VixxoAccent,
                            unfocusedBorderColor = VixxoTextSecondary.copy(alpha = 0.4f),
                            focusedTextColor = VixxoTextPrimary,
                            unfocusedTextColor = VixxoTextPrimary
                        )
                    )

                    Spacer(modifier = Modifier.width(8.dp))

                    OutlinedTextField(
                        value = portText,
                        onValueChange = { portText = it },
                        label = { Text(stringResource(R.string.field_server_port), fontSize = 12.sp) },
                        modifier = Modifier.weight(0.3f),
                        enabled = streamState == StreamState.IDLE,
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedBorderColor = VixxoAccent,
                            unfocusedBorderColor = VixxoTextSecondary.copy(alpha = 0.4f),
                            focusedTextColor = VixxoTextPrimary,
                            unfocusedTextColor = VixxoTextPrimary
                        )
                    )
                }

                // PIN Field
                OutlinedTextField(
                    value = pinText,
                    onValueChange = { pinText = it },
                    label = { Text("6-digit PIN (Optional if already paired)", fontSize = 12.sp) },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = streamState == StreamState.IDLE,
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = VixxoAccent,
                        unfocusedBorderColor = VixxoTextSecondary.copy(alpha = 0.4f),
                        focusedTextColor = VixxoTextPrimary,
                        unfocusedTextColor = VixxoTextPrimary
                    )
                )

                if (!errorMessage.isNullOrBlank()) {
                    Text(
                        text = errorMessage,
                        color = VixxoError,
                        fontSize = 13.sp
                    )
                }
            }
        }

        // Live Audio Level Meter
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = VixxoSurface),
            shape = RoundedCornerShape(16.dp)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = stringResource(R.string.label_mic_level),
                        color = VixxoTextSecondary,
                        fontSize = 14.sp
                    )
                    Text(
                        text = if (isMuted) "MUTED" else "${(micLevel * 100).toInt()}%",
                        color = if (isMuted) VixxoError else VixxoAccent,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )
                }

                LinearProgressIndicator(
                    progress = { if (isMuted) 0f else micLevel },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(10.dp)
                        .clip(RoundedCornerShape(5.dp)),
                    color = if (micLevel > 0.85f) VixxoWarning else VixxoAccent,
                    trackColor = VixxoBackground
                )

                if (streamState == StreamState.STREAMING) {
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = "Connected to: $serverName",
                        color = VixxoTextPrimary,
                        fontSize = 13.sp
                    )
                    Text(
                        text = "Frames Sent: $framesSent (${framesSent * 20 / 1000}s)",
                        color = VixxoTextSecondary,
                        fontSize = 12.sp
                    )
                }
            }
        }

        // Software Gain Slider
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = VixxoSurface),
            shape = RoundedCornerShape(16.dp)
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = stringResource(R.string.label_gain),
                        color = VixxoTextSecondary,
                        fontSize = 14.sp
                    )
                    Text(
                        text = "${gainDb.toInt()} dB",
                        color = VixxoAccent,
                        fontSize = 14.sp,
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
                        inactiveTrackColor = VixxoBackground
                    )
                )
            }
        }

        Spacer(modifier = Modifier.weight(1f))

        // Action Buttons
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            // Mute Button
            OutlinedButton(
                onClick = onToggleMute,
                modifier = Modifier
                    .weight(0.35f)
                    .height(56.dp),
                shape = RoundedCornerShape(14.dp),
                enabled = streamState == StreamState.STREAMING,
                colors = ButtonDefaults.outlinedButtonColors(
                    contentColor = if (isMuted) VixxoError else VixxoTextPrimary
                )
            ) {
                Icon(
                    imageVector = if (isMuted) Icons.Default.MicOff else Icons.Default.Mic,
                    contentDescription = "Mute"
                )
                Spacer(modifier = Modifier.width(6.dp))
                Text(if (isMuted) "Unmute" else "Mute")
            }

            // Start / Stop Main Stream Button
            Button(
                onClick = {
                    if (streamState == StreamState.STREAMING || streamState == StreamState.CONNECTING) {
                        onStopStream()
                    } else {
                        val portInt = portText.toIntOrNull() ?: 47800
                        val pin = pinText.replace(" ", "").trim().ifEmpty { null }
                        onStartStream(hostText.trim(), portInt, pin, pinnedFingerprint)
                    }
                },
                modifier = Modifier
                    .weight(0.65f)
                    .height(56.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (streamState == StreamState.STREAMING) VixxoError else VixxoAccent
                )
            ) {
                Icon(
                    imageVector = if (streamState == StreamState.STREAMING) Icons.Default.Stop else Icons.Default.PlayArrow,
                    contentDescription = null
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = if (streamState == StreamState.STREAMING) stringResource(R.string.btn_stop_stream) else stringResource(R.string.btn_start_stream),
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold
                )
            }
        }

        // Test Local Mic Button (Without connecting to PC)
        OutlinedButton(
            onClick = onToggleLocalTest,
            modifier = Modifier
                .fillMaxWidth()
                .height(48.dp),
            shape = RoundedCornerShape(12.dp),
            enabled = streamState == StreamState.IDLE
        ) {
            Text(
                text = if (isLocalTesting) stringResource(R.string.btn_stop_test) else stringResource(R.string.btn_test_mic),
                color = if (isLocalTesting) VixxoWarning else VixxoTextSecondary
            )
        }
    }

    // Auto-Discovery Dialog
    if (showDiscoveryDialog) {
        AlertDialog(
            onDismissRequest = { showDiscoveryDialog = false },
            title = { Text("Available PCs on Wi-Fi", color = VixxoTextPrimary, fontWeight = FontWeight.Bold) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (isDiscovering) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            CircularProgressIndicator(color = VixxoAccent, modifier = Modifier.width(20.dp).height(20.dp))
                            Spacer(modifier = Modifier.width(10.dp))
                            Text("Searching for Vixxo on Wi-Fi...", color = VixxoTextSecondary, fontSize = 13.sp)
                        }
                    } else if (discoveredServers.isEmpty()) {
                        Text("No PCs found. Make sure Vixxo is open on your PC and both devices are on the same Wi-Fi.", color = VixxoTextSecondary, fontSize = 13.sp)
                    } else {
                        LazyColumn(verticalArrangement = Arrangement.spacedBy(6.dp)) {
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
                                    colors = CardDefaults.cardColors(containerColor = VixxoBackground)
                                ) {
                                    Column(modifier = Modifier.padding(10.dp)) {
                                        Text(server.serverName, fontWeight = FontWeight.Bold, color = VixxoAccent, fontSize = 14.sp)
                                        Text("${server.host}:${server.tcpPort}", color = VixxoTextSecondary, fontSize = 12.sp)
                                    }
                                }
                            }
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = onDiscoverClick) {
                    Text("Refresh", color = VixxoAccent)
                }
            },
            dismissButton = {
                TextButton(onClick = { showDiscoveryDialog = false }) {
                    Text("Cancel", color = VixxoTextSecondary)
                }
            },
            containerColor = VixxoSurface
        )
    }
}

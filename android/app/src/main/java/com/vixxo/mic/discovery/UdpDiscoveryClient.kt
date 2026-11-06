package com.vixxo.mic.discovery

import android.content.Context
import android.net.wifi.WifiManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.NetworkInterface

data class DiscoveredServer(
    val serverName: String,
    val host: String,
    val tcpPort: Int,
    val fingerprint: String
)

class UdpDiscoveryClient(
    private val context: Context? = null,
    private val discoveryPort: Int = 47801,
    private val timeoutMs: Int = 2000
) {
    suspend fun discoverServers(clientName: String): List<DiscoveredServer> = withContext(Dispatchers.IO) {
        val servers = mutableListOf<DiscoveredServer>()
        var socket: DatagramSocket? = null
        var multicastLock: WifiManager.MulticastLock? = null

        try {
            // Acquire MulticastLock if context available (required on Android to receive UDP broadcasts)
            if (context != null) {
                try {
                    val wifiManager = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
                    multicastLock = wifiManager?.createMulticastLock("VixxoDiscoveryLock")?.apply {
                        setReferenceCounted(false)
                        acquire()
                    }
                } catch (_: Exception) {}
            }

            socket = DatagramSocket().apply {
                broadcast = true
                soTimeout = timeoutMs
            }

            // Create discovery request JSON
            val reqJson = JSONObject().apply {
                put("magic", "VIXXO_DISC")
                put("type", "DISCOVER_REQUEST")
                put("client_name", clientName)
            }.toString()

            val sendBytes = reqJson.toByteArray(Charsets.UTF_8)

            // Gather broadcast targets: limited broadcast (255.255.255.255) + local subnet broadcast
            val broadcastTargets = mutableListOf<InetAddress>()
            try {
                broadcastTargets.add(InetAddress.getByName("255.255.255.255"))
            } catch (_: Exception) {}

            try {
                val ifaces = NetworkInterface.getNetworkInterfaces()
                while (ifaces.hasMoreElements()) {
                    val iface = ifaces.nextElement()
                    if (iface.isLoopback || !iface.isUp) continue
                    for (addr in iface.interfaceAddresses) {
                        val bcast = addr.broadcast
                        if (bcast != null && !broadcastTargets.contains(bcast)) {
                            broadcastTargets.add(bcast)
                        }
                    }
                }
            } catch (_: Exception) {}

            for (target in broadcastTargets) {
                try {
                    val sendPacket = DatagramPacket(sendBytes, sendBytes.size, target, discoveryPort)
                    socket.send(sendPacket)
                } catch (_: Exception) {}
            }

            val receiveBuffer = ByteArray(2048)
            val receivePacket = DatagramPacket(receiveBuffer, receiveBuffer.size)
            val endTime = System.currentTimeMillis() + timeoutMs

            while (System.currentTimeMillis() < endTime) {
                try {
                    socket.receive(receivePacket)
                    val respText = String(receivePacket.data, 0, receivePacket.length, Charsets.UTF_8)
                    val json = JSONObject(respText)

                    if (json.optString("magic") == "VIXXO_DISC" && json.optString("type") == "DISCOVER_RESPONSE") {
                        val serverName = json.optString("server_name", "Windows PC")
                        val tcpPort = json.optInt("tcp_port", 47800)
                        val fingerprint = json.optString("fingerprint", "")
                        val remoteIp = receivePacket.address.hostAddress ?: "127.0.0.1"

                        val discovered = DiscoveredServer(serverName, remoteIp, tcpPort, fingerprint)
                        if (!servers.any { it.host == discovered.host && it.tcpPort == discovered.tcpPort }) {
                            servers.add(discovered)
                        }
                    }
                } catch (_: java.net.SocketTimeoutException) {
                    break
                }
            }
        } catch (ex: Exception) {
            android.util.Log.w("VixxoDiscovery", "Discovery error: ${ex.message}")
        } finally {
            socket?.close()
            try {
                if (multicastLock?.isHeld == true) {
                    multicastLock.release()
                }
            } catch (_: Exception) {}
        }

        servers
    }
}

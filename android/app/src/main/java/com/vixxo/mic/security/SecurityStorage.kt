package com.vixxo.mic.security

import android.content.Context
import java.util.UUID

class SecurityStorage(context: Context) {
    private val prefs = context.getSharedPreferences("vixxo_security_store", Context.MODE_PRIVATE)

    val deviceId: String
        get() {
            var id = prefs.getString("device_id", null)
            if (id.isNullOrBlank()) {
                id = UUID.randomUUID().toString()
                prefs.edit().putString("device_id", id).apply()
            }
            return id
        }

    fun getDeviceCredential(host: String): String? {
        // Return host-specific credential, or fall back to last paired credential
        return prefs.getString("cred_$host", null) ?: prefs.getString("last_credential", null)
    }

    fun saveDeviceCredential(host: String, credential: String) {
        prefs.edit()
            .putString("cred_$host", credential)
            .putString("last_credential", credential)
            .apply()
    }

    fun getPinnedFingerprint(host: String): String? {
        return prefs.getString("fp_$host", null) ?: prefs.getString("last_fp", null)
    }

    fun savePinnedFingerprint(host: String, fingerprint: String) {
        prefs.edit()
            .putString("fp_$host", fingerprint)
            .putString("last_fp", fingerprint)
            .apply()
    }

    fun clearCredential(host: String) {
        prefs.edit()
            .remove("cred_$host")
            .remove("fp_$host")
            .remove("last_credential")
            .remove("last_fp")
            .apply()
    }
}

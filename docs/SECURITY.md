# Vixxo - Security Architecture & Threat Model

## 1. Principles
- **Local Only**: Vixxo operates entirely within local network interfaces (LAN, Wi-Fi hotspot, USB loopback). There is no internet server, no telemetry, no analytics, and no remote cloud connectivity.
- **Zero Silent Recording**: Microphone capture on Android requires an explicit user action to start, operates under a persistent foreground service with user-visible notification, and terminates immediately upon user stop.
- **Authentication Required**: Audio packets are only processed after a connection successfully authenticates using a trusted device credential or verified pairing flow.

---

## 2. Cryptographic Architecture

### 2.1 Transport Encryption
- Windows server uses standard platform TLS (`System.Net.Security.SslStream`).
- Self-signed X.509 certificate generated during server initialization (RSA 2048-bit or ECDSA P-256).
- Android client validates the server's certificate against a pinned SHA-256 Subject Public Key Info (SPKI) fingerprint obtained during pairing.
- Wildcard "accept all certificates" handlers are strictly prohibited.

### 2.2 Pairing Flow
1. **QR Pairing**:
   - PC displays a QR code containing:
     - PC IP and Port
     - SPKI SHA-256 Fingerprint
     - Cryptographically random 128-bit pairing token (Base64)
     - Expiration timestamp (valid for 3 minutes)
   - Android scans QR, connects via TLS, verifies the server's SPKI fingerprint against the QR payload, and sends the pairing token.
   - Upon verification, the PC issues a persistent, cryptographically secure 256-bit `deviceCredential`.
2. **Manual Pairing**:
   - User enters IP and Port.
   - Server presents a 6-digit random PIN on the PC screen (expires in 3 minutes, maximum 5 attempts).
   - Phone displays the server SPKI fingerprint for user confirmation, then sends the PIN.
   - Upon successful verification, the PC issues a persistent `deviceCredential`.

### 2.3 Secret Storage
- **Android**: Device credentials and tokens are stored in EncryptedSharedPreferences backed by the **Android Keystore System** (`MasterKeys.AES256_GCM`).
- **Windows**: Device credentials and server private keys are stored in encrypted form using the **Windows Data Protection API (DPAPI)** (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`).
- Non-secret preferences (theme, UI options, device nicknames) reside in standard JSON/DataStore settings files.

---

## 3. Revocation & Access Control
- The Windows Settings panel provides a **Trusted Devices** list.
- A user can revoke any paired phone at any time. Revocation immediately invalidates that device's credential and disconnects any active stream.
- Re-connection requires a fresh pairing procedure.

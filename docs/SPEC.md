# Vixxo - Project Specification

Working product name: vixxo (PhoneMic)

## Essential Outcome
The user speaks into an Android phone, and real Windows applications—Windows Sound Recorder, Discord, Zoom, OBS, and Teams—receive that audio through a selectable microphone device (VB-CABLE Virtual Audio Device).

---

## 1. Non-Negotiable Working Rules

1. Build an actual working product, not a UI demonstration.
2. Never replace real audio, connectivity, device detection, or measurements with simulated production behavior. Synthetic audio is allowed ONLY in clearly identified test tools.
3. Do not report success merely because code was generated. Distinguish:
   - Implemented
   - Build passed
   - Automated tests passed
   - Hardware verification pending
   - Hardware verification passed
   - Blocked
4. Never invent terminal output, test results, device availability, package APIs, latency measurements, or installer signing.
5. Work phase by phase using the checkpoints below. Within a phase, autonomously implement, build, test, diagnose, and fix. Do not stop after every file. At the end of a phase, give a concise report and wait for user's "go".
6. If a build or test fails:
   - Read the actual error.
   - Diagnose the root cause.
   - Apply the smallest justified fix.
   - Re-run the affected checks.
   - Run relevant regression tests.
   Do not repeatedly regenerate the entire project.
7. Do not perform large rewrites, change the agreed stack, or introduce a new dependency without a concrete technical reason.
8. Do not delete existing unrelated files. If the selected folder is not empty, inspect it and propose a safe new subfolder. Never delete old projects on the user's behalf.
9. Do not disable Windows security, Android security, firewall protection, antivirus, driver-signing enforcement, or TLS verification to make something work.
10. Ask user only for decisions or actions that genuinely require user: hardware access, permissions, admin approval, signing credentials, or unresolved product choices.
11. If the environment cannot run a required Windows or Android tool: explain the exact limitation and continue with independent work where possible. Never mark the unavailable check as passed.
12. Do not spend the project building elaborate architecture or attractive screens before proving the audio path.

---

## 2. Project Source of Truth

Maintained documentation:
- docs/SPEC.md
- docs/PROGRESS.md
- docs/DECISIONS.md
- docs/ENVIRONMENT.md
- docs/PROTOCOL.md
- docs/TESTING.md
- docs/TROUBLESHOOTING.md
- docs/SECURITY.md
- docs/THIRD_PARTY_NOTICES.md
- README.md

---

## 3. Scope and Tech Stack

### Required Release
- Android phone application
- Windows desktop application
- Wi-Fi streaming
- USB streaming through adb reverse
- Phone-hotspot compatibility
- QR pairing
- Manual connection
- LAN discovery
- Virtual microphone integration
- Audio controls
- Background streaming
- Reconnection
- Windows installer
- Android APK
- Build scripts and documentation

### Optional after required release works
- Opus compression
- Bluetooth RFCOMM

### Android Tech Stack
- Kotlin
- Jetpack Compose and Material 3
- ViewModel, StateFlow, coroutines
- DataStore for non-secret settings
- AudioRecord
- CameraX and verified QR-scanning library
- minSdk 26
- Stable, mutually compatible Android SDK, AGP, Kotlin, Compose, Gradle, and JDK versions

### Windows Tech Stack
- C#
- Supported LTS .NET (8.0 LTS)
- WPF
- MVVM
- NAudio for supported Windows audio operations
- QRCoder
- System.Text.Json
- Verified maintained tray implementation (Hardcodet.NotifyIcon.Wpf)
- WPF-UI only if verified compatible

### Installer
- Inno Setup 6
- Self-contained Windows application deployment (win-x64)

### Target
- Windows 10/11 x64
- Physical Android phones running supported Android versions (API 26+)

### Repository Layout
- `/android`
- `/windows`
- `/tests`
- `/tools`
- `/installer`
- `/scripts`
- `/docs`
- `/artifacts`

---

## 4. Windows Virtual Microphone Architecture

Routing:
Phone audio
→ Windows Vixxo receiver
→ VB-CABLE playback endpoint, normally "CABLE Input"
→ VB-CABLE recording endpoint, normally "CABLE Output"
→ Discord / Zoom / OBS / Sound Recorder / Teams

Endpoint naming clarity:
- Vixxo sends audio to "CABLE Input".
- Other applications select "CABLE Output" as their microphone.
- Do not reverse this routing.

Requirements:
- Detect playback and recording endpoints using MMDevice identifiers.
- Manual playback endpoint selection with persistent ID handling.
- Clear missing-driver state.
- Refresh/retry capability.
- Never silently route microphone audio to speakers if virtual cable disappears.
- Never alter Windows default audio devices without explicit permission.
- Explicit self-test playing a short, safe-level test tone into selected virtual playback endpoint.
- Optional local monitoring (OFF by default, separate monitor endpoint, headphones recommended).

---

## 5. Network and Protocol

- Server: Windows PC
- Client: Android phone
- TCP control/audio port: 47800 (configurable)
- UDP discovery port: 47801 (configurable)
- Transports: TCP with TCP_NODELAY (supports Wi-Fi, Hotspot, and USB via `adb reverse`)
- Security: Standard TLS over TCP using standard platform cryptography.

Framing:
- Magic: ASCII "PM" (0x50, 0x4D), 2 bytes
- Protocol version: 1 byte (0x01)
- Message type: 1 byte
- Payload length: uint32 little-endian (4 bytes)
- Fixed header length: 8 bytes

Message types:
- HELLO (0x01)
- AUTH (0x02)
- WELCOME (0x03)
- CONFIG (0x04)
- START (0x05)
- STOP (0x06)
- AUDIO (0x07)
- PING (0x08)
- PONG (0x09)
- STATS (0x0A)
- CONTROL (0x0B)
- ERROR (0x0C)
- BYE (0x0D)

---

## 6. Pairing and Security

- QR pairing: Contains version, host, port, PC name, short-lived random token, server certificate SPKI fingerprint.
- Strict QR payload validation.
- Standard TLS certificate pinning on Android client.
- Manual pairing: PC IP/port, server fingerprint verification, 6-digit PIN with attempt limit and expiration.
- Device credentials issued after pairing and stored in Android Keystore / Windows Data Protection API (DPAPI).
- Trusted device revocation.
- No analytics, cloud upload, or telemetry.

---

## 7. Audio Pipeline

Baseline format:
- PCM signed 16-bit little-endian
- 48 kHz
- Mono
- 20 ms frames (960 samples = 1920 bytes per frame)

Android capture:
- AudioRecord on dedicated audio thread (priority `THREAD_PRIORITY_URGENT_AUDIO`).
- Bounded capture-to-network queue (no network I/O on audio thread).
- Audio sources: VOICE_COMMUNICATION / MIC / UNPROCESSED.
- Phone effects: NoiseSuppressor, AcousticEchoCanceler, AutomaticGainControl, Software gain, Mute.

Windows rendering:
- WASAPI shared-mode output to CABLE Input.
- Event-driven rendering with bounded buffer.
- Continuous render behavior with underrun silence and drift compensation.
- Latency modes: Low (40-60 ms buffer), Balanced (80-100 ms buffer), Stable (140-180 ms buffer).
- DSP: Optional high-pass filter, noise gate, gain, limiter, real RMS/peak meter.

---

## 8. USB Mode

- `adb -s <serial> reverse tcp:<port> tcp:<port>`
- Phone connects to `127.0.0.1:<port>`.
- Full TLS and authentication preserved.
- Automatic reconnection and clean cleanup of owned reverse mappings.

---

## 9. Firewall & Robustness

- Application/port-scoped firewall rules (local subnet / private profile).
- Bounded exponential backoff reconnection.
- Graceful recovery from sleep, network changes, device replug.

---

## 10. Phases and Deliverables

- PHASE 0: Environment and specification (Current)
- PHASE 1: Windows audio vertical slice
- PHASE 2: Android real-microphone vertical slice
- PHASE 3: Complete functional UI
- PHASE 4: Convenient pairing and LAN behavior
- PHASE 5: USB Mode
- PHASE 6: Quality and reliability
- PHASE 7: Packaging (Inno Setup & Android APK)
- PHASE 8: Final acceptance

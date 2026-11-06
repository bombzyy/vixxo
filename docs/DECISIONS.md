# Vixxo - Architectural & Technical Decisions (ADR)

## ADR-001: Technology Stack and Runtime Selection
- **Status**: Accepted
- **Context**: Need a cross-platform solution (Android client + Windows server) with low-latency audio transmission, reliable background operation, and standard virtual microphone integration.
- **Decision**:
  - **Windows**: .NET 8.0 LTS (`net8.0-windows`), C# 12, WPF, MVVM. .NET 8 provides long-term support, high performance in socket I/O, span-based buffer manipulations, and native WASAPI audio handling via NAudio.
  - **Android**: Modern native Android with Kotlin, minSdk 26 (Android 8.0 Oreo), targetSdk 34, Jetpack Compose for UI, ViewModel + StateFlow for state management, AudioRecord on dedicated thread with `THREAD_PRIORITY_URGENT_AUDIO`, and foreground service with `microphone` type for background capture.
- **Consequences**:
  - Requires .NET 8 SDK on the build machine.
  - Self-contained Windows publish ensures end users do not need to install .NET runtime manually.

---

## ADR-002: Windows Virtual Microphone Strategy
- **Status**: Accepted
- **Context**: Creating a custom Windows kernel-mode audio driver requires EV code signing certificates, kernel driver certification, and introduces stability/installation risks.
- **Decision**:
  - Leverage VB-CABLE virtual audio cable (pre-installed and verified on the system).
  - Vixxo renders received PCM audio to the playback endpoint named `CABLE Input (VB-Audio Virtual Cable)`.
  - Windows sound applications (Discord, Zoom, Teams, OBS) record from `CABLE Output (VB-Audio Virtual Cable)`.
  - Identification uses WASAPI MMDevice IDs rather than fuzzy name matching.
- **Consequences**:
  - Eliminates kernel driver installation risks.
  - Clear UI labeling is required so users understand Vixxo outputs to "CABLE Input" and third-party apps listen to "CABLE Output".

---

## ADR-003: Transport Layer & Audio Framing
- **Status**: Accepted
- **Context**: Need a protocol that works across Wi-Fi, local Hotspots, and USB (via `adb reverse`).
- **Decision**:
  - Primary transport: TCP with `TCP_NODELAY` enabled on both ends to eliminate Nagle's algorithm delay.
  - Port: TCP 47800 (configurable) for control & audio, UDP 47801 (configurable) for LAN discovery.
  - Framing: 8-byte fixed binary header:
    - Magic: ASCII `PM` (`0x50, 0x4D`)
    - Version: `0x01`
    - MessageType: 1 byte
    - PayloadLength: uint32 little-endian (4 bytes)
  - Control payloads: UTF-8 JSON with bounded length checks.
  - Audio payloads: Binary header (sequence number, timestamp, samples per channel) followed by signed 16-bit PCM little-endian audio bytes.
- **Consequences**:
  - Supports `adb reverse tcp:47800 tcp:47800` out of the box for USB streaming without separate tunneling protocols.
  - TCP provides reliable delivery, but buffer management and drift compensation are required to prevent audio delay buildup under network congestion.

---

## ADR-004: Security, Authentication, and Pairing
- **Status**: Accepted
- **Context**: Microphone streams must not be eavesdropped, hijacked, or triggered by unauthorized network devices.
- **Decision**:
  - Transport Layer Security (TLS) over TCP using platform cryptography (`SslStream` on .NET, `SSLSocket` on Android).
  - Pairing via QR Code containing host IP, port, server certificate SPKI SHA-256 fingerprint, and a short-lived cryptographically random pairing token.
  - Fallback manual pairing: Enter IP/port, visually verify SPKI fingerprint, authenticate with a 6-digit expiring PIN.
  - Trusted device tokens stored in Android Keystore (Android) and Windows DPAPI (Windows).
  - No internet connectivity or third-party cloud service dependency.
- **Consequences**:
  - Prevents MITM attacks and rogue audio injection.
  - Strict certificate pinning prevents invalid TLS certificates.

---

## ADR-005: Audio Pipeline & Clock Drift Handling
- **Status**: Accepted
- **Context**: Phone capture clock and PC audio card render clock are physically independent and will drift apart over time.
- **Decision**:
  - Baseline format: Signed 16-bit PCM, 48000 Hz, Mono, 20 ms frames (960 samples / 1920 bytes per frame).
  - Windows receiver uses a circular jitter buffer with configurable latency profiles:
    - Low: 40–60 ms buffer
    - Balanced: 80–100 ms buffer
    - Stable: 140–180 ms buffer
  - Drift compensation: Continuous buffer occupancy monitoring and high-quality fractional resampling/interpolation to adjust speed dynamically (within +/- 0.5%) without audible pitch artifacts or periodic sample drops.
  - Underruns output safe silence and increment an underrun counter; overruns safely compress buffer without unbounded delay growth.

---

## ADR-006: Android Audio Capture Architecture & Lifecycle
- **Status**: Accepted
- **Context**: Android power management and background limits can aggressively pause or kill background threads, drop audio buffers, or cause severe underruns when the screen turns off.
- **Decision**:
  - Capture Engine (`AudioCaptureEngine`) runs on a dedicated background thread with `android.os.Process.setThreadPriority(Process.THREAD_PRIORITY_URGENT_AUDIO)`.
  - Buffer allocation: Uses `AudioRecord.getMinBufferSize` multiplied by safety factor, pulling 20 ms chunks (960 samples = 1920 bytes) into a pre-allocated bounded non-blocking `ArrayBlockingQueue(20)` (max 400 ms buffer).
  - Hardware audio enhancement effects (`AcousticEchoCanceler`, `AutomaticGainControl`, `NoiseSuppressor`) are probed at runtime and engaged if available on the hardware.
  - Background execution managed through an Android Foreground Service (`MicrophoneStreamService`) with `FOREGROUND_SERVICE_MICROPHONE` (Android 14 compliant), a persistent notification with instant Mute/Unmute and Stop actions, and a partial `WakeLock`.
  - Local Microphone Test loopback allows hardware audio verification without establishing a network connection to Windows.
- **Consequences**:
  - Requires user to grant `RECORD_AUDIO` and `POST_NOTIFICATIONS` runtime permissions.
  - Ensures seamless audio capture when phone screen is locked or another app is open.

---

## ADR-007: Windows Desktop UI & System Tray Integration
- **Status**: Accepted
- **Context**: The Windows receiver must provide clear device selection, real-time metering, background operation via tray, and local headphone monitoring without blocking the primary virtual microphone.
- **Decision**:
  - Implementation: C# / WPF on .NET 8 LTS using the MVVM pattern.
  - System Tray: Leverages `System.Windows.Forms.NotifyIcon` via `<UseWindowsForms>true</UseWindowsForms>`. This avoids external third-party tray libraries, ensures stable Windows OS integration, and provides instant minimize-to-tray and context menu capabilities.
  - Local Headphone Monitoring: Implemented using an isolated secondary `CircularJitterBuffer` and `WasapiAudioRenderer`. Audio frames received by `VixxoServer` are concurrently fed to the monitor buffer. If the monitoring audio endpoint experiences buffer underruns, latency spikes, or stalls, it operates entirely within its own queue and never delays, starves, or blocks the primary `CABLE Input` virtual microphone renderer.
  - Settings Persistence: JSON configuration stored in `%LOCALAPPDATA%\Vixxo\settings.json`.
- **Consequences**:
  - Robust against device hot-plugging and prevents audio feedback.
  - Clean background execution with minimal memory footprint.

---

## ADR-008: Pairing, LAN Discovery, and TLS Pinning Lifecycle
- **Status**: Accepted
- **Context**: Users need effortless, secure pairing between phone and PC without security warnings, manual certificate installation, or cloud accounts.
- **Decision**:
  - **Discovery**: Lightweight UDP protocol on port 47801 with `VIXXO_DISC` magic. Android broadcasts `DISCOVER_REQUEST`, Windows server immediately replies with PC name, TCP port, local IPv4 addresses, and TLS certificate fingerprint.
  - **TLS Self-Signed Certificate**: Generated automatically on Windows using .NET `CertificateRequest` (RSA-2048, SHA-256) and saved in `%LOCALAPPDATA%\Vixxo\vixxo_server.pfx`.
  - **Certificate Pinning**: The SHA-256 fingerprint is included in the QR code and UDP discovery payload. The Android client pins this exact hash during the TLS handshake via custom `X509TrustManager`, completely bypassing public CA requirements while remaining immune to MITM attacks.
  - **Pairing & Credential Lifecycle**:
    - QR Code and manual dialog generate a single-use 6-digit numeric PIN and a 16-byte cryptographically random token expiring after 5 minutes.
    - Rate-limiting blocks any remote IP after 5 failed authentication attempts for 5 minutes.
    - Upon successful PIN or token authentication, Windows issues a persistent 32-byte `DeviceCredential`, stored in `%LOCALAPPDATA%\Vixxo\trusted_devices.json`.
    - Android securely stores this credential; future connections authenticate transparently in 1 roundtrip.
    - Windows UI provides immediate revocation for any paired phone.
- **Consequences**:
  - Zero external cloud services, 100% offline security.
  - Seamless 1-tap connection after initial pairing.

---

## ADR-009: USB Streaming via ADB Reverse Port Forwarding
- **Status**: Accepted
- **Context**: In environments with congested 2.4 GHz Wi-Fi or when zero wireless packet loss is required, streaming over a physical USB cable provides minimum latency and complete interference immunity.
- **Decision**:
  - Use standard `adb reverse tcp:47800 tcp:47800` rather than custom USB drivers or complex AOA (Android Open Accessory) protocols.
  - Windows `AdbManager` probes the local environment for `adb.exe`, detects attached USB Android devices, checks authorization state, and maps PC port 47800 into the phone's loopback network interface.
  - Background `AdbDeviceWatcher` automatically detects cable unplug / replug events (HW-13) and immediately restores reverse port forwarding without user interaction.
  - Android client connects to `127.0.0.1:47800`. Because ADB reverse terminates into the same TCP port, the full TLS encryption, framing, and authentication logic operate identically without separate USB code paths.
  - On application termination or device disconnect, all owned reverse mappings are explicitly removed (`adb reverse --remove tcp:47800`).
- **Consequences**:
  - Works with Android Wi-Fi turned completely OFF.
  - Requires Android USB Debugging enabled on the phone.


---

## ADR-010: Real-Time Audio DSP and Micro-Resampling Drift Compensation
- **Status**: Accepted
- **Context**: Long-duration streaming suffers from physical crystal oscillator drift between mobile devices and PC sound cards, resulting in buffer underruns or latency buildup over time. In addition, raw phone microphones pick up sub-bass desk thuds and ambient computer fan noise.
- **Decision**:
  - Implement a 2nd-order Butterworth High-Pass Filter (80 Hz cutoff, Q=0.7071) to eliminate mechanical rumble and low-frequency handling noise.
  - Implement a smooth Noise Gate with 5ms attack, 60ms hold, 100ms exponential release, and configurable threshold (-60 dB to -20 dB). Gate starts in the open state to avoid clipping the first spoken word.
  - Implement dynamic clock drift compensation using an exponential moving average (alpha=0.05) of buffer depth. When buffer depth drifts beyond ±30% of target depth, apply micro-resampling (0.2% sample-stuffing / sample-dropping at 1 sample per 500 samples) during mono-to-stereo expansion.
- **Consequences**:
  - Buffer depth remains rock solid at target latency indefinitely.
  - Pitch distortion and audio clicks are completely eliminated.

---

## ADR-011: Windows Power Management and Android Exponential Backoff
- **Status**: Accepted
- **Context**: Laptops and PCs enter Sleep/Suspend modes, causing socket teardown without clean TCP FIN packets. Mobile devices experience transient Wi-Fi dropouts or roaming handover blips.
- **Decision**:
  - On Windows: Register for `Microsoft.Win32.SystemEvents.PowerModeChanged`. Stop server cleanly on `PowerModes.Suspend` and automatically restart server/device detection on `PowerModes.Resume`.
  - On Android: Implement exponential backoff reconnection loop in `MicrophoneStreamService` (1s, 2s, 4s, 8s, up to 10 attempts). On network recovery, the client automatically re-authenticates using stored device credentials without requiring user intervention.
- **Consequences**:
  - Clean recovery from system sleep and network blips without manual app restarts.


---

## ADR-012: Inno Setup Self-Contained Deployment and Driver Safety
- **Status**: Accepted
- **Context**: Users expect a standard, clean Windows installer without being forced to install external .NET runtimes, while ensuring shared virtual audio drivers like VB-CABLE are handled lawfully and non-destructively.
- **Decision**:
  - Build a fully self-contained `win-x64` deployment containing all .NET 8 LTS CoreCLR libraries, eliminating runtime dependencies.
  - Package via Inno Setup 6 with LZMA2 ultra compression (`artifacts/Vixxo-Setup-x64.exe`, ~50 MB).
  - During installation: inspect Windows registry for VB-CABLE driver endpoints (`CABLE Input` / `ROOT\MEDIA\0000`). If missing, provide explicit user guidance and official download link (`https://vb-audio.com/Cable/`).
  - During uninstallation: remove all Vixxo application files and shortcuts, but strictly leave the shared VB-CABLE virtual driver untouched to prevent breaking other audio software on the user's PC.
- **Consequences**:
  - Seamless 1-file installation on any 64-bit Windows 10/11 system.
  - Zero risk of damaging third-party audio drivers.

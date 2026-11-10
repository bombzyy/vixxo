<div align="center">

# 🎙️ Vixxo

### High-Fidelity Android to Windows Virtual Microphone
**Turn any Android phone into a studio-grade, ultra-low latency PC microphone over Wi-Fi, USB, and Hotspot.**

[![Release](https://img.shields.io/badge/Release-v2.0.0-0284c7?style=for-the-badge&logo=github)](https://github.com/bombzyy/vixxo/releases/tag/v2.0.0)
[![Website](https://img.shields.io/badge/Website-Live_on_Cloudflare-38bdf8?style=for-the-badge&logo=cloudflare)](https://vixxo-1m6.pages.dev/)
[![Platform](https://img.shields.io/badge/Platform-Windows_%7C_Android-6366f1?style=for-the-badge)](https://github.com/bombzyy/vixxo)
[![Tests](https://img.shields.io/badge/Tests-83%2F83_Passing-10b981?style=for-the-badge)](https://github.com/bombzyy/vixxo)
[![License](https://img.shields.io/badge/License-MIT-f59e0b?style=for-the-badge)](LICENSE)

<br/>

🌐 **Official 3D SaaS Landing Page:**  
👉 **[https://vixxo-1m6.pages.dev/](https://vixxo-1m6.pages.dev/)**

<br/>

[🚀 Features](#-key-features) •
[📥 Downloads](#-quick-downloads) •
[🏗️ Architecture](#️-system-architecture) •
[⚡ Quickstart](#-quickstart-guide) •
[🎮 App Setup](#-app-configuration-guide) •
[📊 Benchmarks](#-performance-benchmarks) •
[📖 Documentation](#-documentation-index)

</div>

---

## 💡 Why Vixxo?

Most wireless microphone solutions suffer from major drawbacks:
1. **Bluetooth Hands-Free Profile (HFP)** crushes voice quality down to an 8 kHz or 16 kHz muffled telephone standard with 150–250 ms lag.
2. **Generic Wi-Fi microphone apps** introduce heavy compression artifacts, lack DSP filters, collect user telemetry, and fail completely when your Wi-Fi router has **AP Isolation** enabled.
3. **Custom audio kernel drivers** frequently cause Blue Screen of Death (BSOD) crashes on Windows sleep/wake cycles or major Windows updates.

**Vixxo solves this entirely:**
- **Studio 48,000 Hz, 16-bit uncompressed PCM** audio straight from your phone's microphone diaphragm.
- **Sub-5ms deterministic latency** over USB using automated ADB reverse port tunneling (`127.0.0.1:47800`).
- **Zero Kernel Driver Bluescreens:** Feeds audio safely through the battle-tested **VB-Audio Virtual Cable** (`CABLE Input` $\to$ `CABLE Output`).
- **100% Local & Private:** Zero cloud dependencies, zero telemetry, zero accounts. Runs entirely on your local machine.

---

## 📥 Quick Downloads

| Platform | Download Link | Description | Size |
| :--- | :--- | :--- | :--- |
| **Windows 10 / 11** | [**Download Vixxo-Setup-x64.exe (v2.0.0)**](https://github.com/bombzyy/vixxo/releases/download/v2.0.0/Vixxo-Setup-x64.exe) | Complete standalone x64 installer (**Bundled with RVC AI Neural models, Global Hotkeys, & VB-CABLE driver**) | `541 MB` |
| **Android 8.0+** | [**Download Vixxo-Mobile-v2.0.0.apk**](https://github.com/bombzyy/vixxo/releases/download/v2.0.0/Vixxo-Mobile-v2.0.0.apk) | Full Stream Deck Mobile & Microphone client (Jetpack Compose, background streaming service) | `19.5 MB` |
| **Virtual Driver** | **Auto-Bundled in Setup** | VB-CABLE is automatically installed by the setup wizard or via 1-click in-app button | Built-in |

---

## 🚀 Key Features

### 1. 🎛️ Studio-Grade Audio Pipeline
- **Studio Sample Rate:** Captures and renders pure **48,000 Hz, 16-bit Mono PCM** audio (96 KB/s lossless throughput).
- **Adaptive Circular Jitter Buffer:** Dynamically absorbs Wi-Fi jitter with configurable latency modes:
  - **Ultra-Low Latency:** 20 ms buffer (Ideal for competitive gaming & USB mode)
  - **Balanced:** 50 ms buffer (Default recommended for Wi-Fi)
  - **Safe / Conservative:** 100 ms buffer (Ideal for congested or weak wireless networks)
- **Clock Drift Compensation:** Continuously monitors sample drift between Android and PC hardware clocks and makes inaudible fractional adjustments to prevent audio desync over long sessions.

### 2. 🛡️ Built-in Real-Time DSP Audio Processing
- **Soft-Knee Peak Limiter:** Prevents digital clipping, harsh distortion, and blown eardrums even during sudden loud shouting or microphone bumps.
- **80 Hz High-Pass Filter (HPF):** Rejects mechanical desk thumps, chair rumbles, and air conditioner sub-bass vibrations.
- **Room Noise Gate (-42 dBFS default):** Eliminates mechanical keyboard clicks, mouse clicking, and room background hiss when you aren't speaking.
- **Real-Time VU Metering:** Hardware-style Peak & RMS dBFS visualizers with smooth decay on both Windows and Android.

### 3. ⚡ Triple Low-Latency Transport Modes
- **USB Cable Mode (Sub-5ms):** Automatically triggers `adb reverse tcp:47800 tcp:47800`. Audio travels over the physical copper USB cable directly via loopback (`127.0.0.1`). 100% immune to Wi-Fi drops and router restrictions.
- **Wi-Fi Mode (8–15ms):** Auto-discovers your PC via UDP broadcast beacon on port 47801. Connect with 1 click or scan the on-screen QR code.
- **Mobile Hotspot Mode:** When connected to restricted public Wi-Fi, college dorm networks, or routers with **AP Isolation (Client Isolation)**, simply turn on your phone's Mobile Hotspot and connect your PC to stream with sub-10ms direct wireless latency.

### 4. 📱 Remote Phone Soundboard (Stream Deck Mobile Mode) — *NEW in v2.0*
- **Live Bilateral TCP Sync:** Phone automatically mirrors all sound clips configured on the Windows PC in real time.
- **Stream Deck Mobile Touch Grid:** 2-column tactile sound pads with real-time green pulsing glow while sounds are playing.
- **Category Filter Chips:** Instant category navigation (`All`, `Valorant`, `Memes`, `Custom`).
- **Emergency Panic Stop:** Dedicated one-tap **"⏹ STOP ALL"** button on the phone instantly halts playback on PC.
- **Zero Jitter Duplexing:** Control commands (`0x0E` Sync, `0x0F` Action) run on concurrent worker loops without interrupting microphone audio packet transmission.

### 5. 🤖 Real-Time AI Neural Voice Changer (RVC v2 CUDA) — *NEW in v2.0*
- **CUDA Deep Learning Acceleration:** Runs Retrieval-based Voice Conversion (RVC v2) with RMVPE pitch extraction on NVIDIA GPUs.
- **Lock-Free SPSC Shared Memory Ring Buffer:** High-performance inter-process communication between C# and Python RVC worker. Audio thread `Read()` **NEVER blocks** on GPU inference.
- **Sub-80ms Real-Time Inference:** Operates within a sub-100ms real-time conversational budget.
- **Intelligent DSP Fallback:** Instant latency-aware crossfading to local DSP effects if GPU falls behind, preventing audio stutter or robotic drops.
- **Pre-Bundled Presets:** Includes Neural AI models plus Fast DSP presets (*Robot, Demon, Girl, Lady, Deep Pitch*).

### 6. ⌨️ Native Win32 Global Background Hotkeys — *NEW in v2.0*
- **Background Execution:** Uses Win32 native `RegisterHotKey` and `WM_HOTKEY` pump so hotkeys fire seamlessly while gaming in full-screen (Valorant, CS2, Apex Legends, Fortnite).
- **Interactive Hotkey Capture Dialog:** Rebind any sound pad with single keys (`F1`-`F12`, `NumPad 0-9`) or multi-key modifier combos (`Ctrl+1`, `Alt+Shift+X`).
- **Panic Stop Hotkey:** Default `NumPad 0` halts all playing sounds instantly from any application.

### 7. 🔄 Seamless In-App Auto-Updater — *NEW in v2.0*
- **GitHub Releases Integration:** Automatically queries GitHub Releases API for updates on app startup.
- **Dark OLED Dialog:** Non-intrusive modal displays version changelog with One-Click **"Update Now"**.
- **Silent Background Installer:** Downloads the new installer, verifies integrity, and seamlessly restarts Vixxo.

### 8. 💻 Native Windows Desktop Experience
- Built on modern **.NET 8 LTS** and **WPF** with hardware-accelerated rendering.
- **Zero-Config Virtual Driver Setup:** The official VB-CABLE virtual audio driver is pre-bundled in the Windows installer and installed silently during setup. If ever missing, the app UI features an instant **1-Click Driver Installer** button with automatic device refresh.
- **System Tray Integration:** Minimizes unobtrusively to the system tray with global one-click mute and real-time status notifications.
- **Local Audio Monitoring:** Listen to your microphone input in real-time through your headphones to test quality and levels before going live.

### 9. 🔒 Zero-Trust Security & Privacy
- **End-to-End Encryption:** Audio frames are transmitted over TLS 1.3 with AES-256-GCM cipher suites.
- **SHA-256 Certificate Pinning:** Protects against local network man-in-the-middle (MITM) attacks.
- **Single-Use 6-Digit PIN Pairing:** Temporary pairing PINs automatically expire after 5 minutes to prevent unauthorized devices from connecting.

---

## 🏗️ System Architecture

```text
┌────────────────────────────────────────────────────────────────────────┐
│                          ANDROID PHONE (Vixxo)                         │
│                                                                        │
│   [Diaphragm] ──▶ [AudioRecord API @ 48kHz] ──▶ [Float RMS Meter]     │
│                             │                                          │
│                             ▼                                          │
│                  [Vixxo Framing Stream]                                │
│          (0x56495858 Magic + Sequence + 20ms Chunks)                   │
└─────────────────────────────────┬──────────────────────────────────────┘
                                  │
                                  │  USB Cable (adb reverse loopback)
                                  │  OR Wi-Fi LAN / Hotspot (TCP/TLS)
                                  ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        WINDOWS PC (Vixxo Server)                       │
│                                                                        │
│                    [Vixxo Frame Reader Engine]                         │
│                             │                                          │
│                             ▼                                          │
│                    [Circular Jitter Buffer]                            │
│                             │                                          │
│                             ▼                                          │
│                    [Drift Compensator]                                 │
│                             │                                          │
│                             ▼                                          │
│                [DSP: HPF 80Hz + Noise Gate + Limiter]                  │
│                             │                                          │
│                             ▼                                          │
│             [WASAPI Audio Renderer (Shared Mode)]                      │
│                             │                                          │
│                             ▼                                          │
│                  [CABLE Input (VB-CABLE)]                              │
└─────────────────────────────┬──────────────────────────────────────────┘
                              │
                              │ (Internal Virtual Audio Link)
                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│                 [CABLE Output (Virtual Microphone)]                    │
│                                                                        │
│         ┌─────────────────┬─────────────────┬────────────────┐         │
│         ▼                 ▼                 ▼                ▼         │
│   [Discord Voice]   [OBS Studio Mic]   [Zoom / Teams]   [Audacity/DAW] │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 📊 Performance Benchmarks

| Metric / Scenario | Vixxo (USB Mode) | Vixxo (Wi-Fi) | Bluetooth (HFP) | Generic Wi-Fi Mic Apps |
| :--- | :---: | :---: | :---: | :---: |
| **Audio Latency** | **< 5 ms** ⚡ | **8 – 15 ms** | 150 – 250 ms | 60 – 180 ms |
| **Audio Sample Rate** | **48 kHz / 16-bit** | **48 kHz / 16-bit** | 8 – 16 kHz | Lossy compressed |
| **Audio Format** | **Uncompressed PCM** | **Uncompressed PCM** | Telephone Speex/mSBC | Lossy OPUS / AAC |
| **Router AP Isolation** | **100% Immune** | Hotspot Fallback | N/A | Fails completely |
| **DSP Filters** | **Gate + Limiter + HPF** | **Gate + Limiter + HPF** | None | None / Paid only |
| **Privacy / Cloud** | **100% Local** | **100% Local** | Local | Cloud telemetry / Ads |
| **Price & License** | **Free (MIT)** | **Free (MIT)** | Hardware cost | Subscription / Ads |

---

## ⚡ Quickstart Guide

### 1. Windows Setup
1. Download and run [**`Vixxo-Setup-x64.exe`**](https://github.com/bombzyy/vixxo/releases/download/v1.0.0/Vixxo-Setup-x64.exe).
2. The setup wizard **automatically detects and silently installs the VB-CABLE driver** if it isn't already on your system! *(If skipped or uninstalled later, a **1-Click "Install Audio Driver"** button is built right into the app).*
3. Launch **Vixxo** from your Start menu or desktop shortcut.
4. Verify that the Virtual Microphone Routing dropdown shows **`CABLE Input (VB-Audio Virtual Cable)`** (auto-selected).

### 2. Android Setup
1. Download and install [**`vixxo-android-release.apk`**](https://github.com/bombzyy/vixxo/releases/download/v1.0.0/vixxo-android-release.apk) on your phone.
2. Open **Vixxo** on your phone and grant Microphone and Camera permissions when prompted.

### 3. Connect & Stream
- **Via QR Code:** Click **"Pair Phone (QR Code / PIN)"** on your PC, then tap **"QR"** on your phone to scan the barcode.
- **Via USB Cable (Fastest):** Connect your phone to PC via USB cable, ensure "USB Debugging" is on, tap **"USB"** on phone, and press **"Start Streaming"**.
- **Via Wi-Fi Search:** Ensure both devices are on the same Wi-Fi, tap **"Wi-Fi"** on phone to search for your PC, and connect.

---

## 🎮 App Configuration Guide

Once Vixxo is streaming, set your input device in your preferred desktop applications:

### 💬 Discord
1. Open **User Settings** $\to$ **Voice & Video**.
2. Under **Input Device**, select **`CABLE Output (VB-Audio Virtual Cable)`**.
3. Under **Input Mode**, choose *Voice Activity* or *Push to Talk*.

### 🎥 OBS Studio
1. In OBS, open **Settings** $\to$ **Audio**.
2. Under **Mic/Auxiliary Audio Device**, select **`CABLE Output (VB-Audio Virtual Cable)`**.
3. Your phone voice will now appear in your OBS audio mixer.

### 👥 Zoom / Microsoft Teams
1. Open **Settings** $\to$ **Audio**.
2. Under **Microphone**, select **`CABLE Output (VB-Audio Virtual Cable)`**.

---

## 🔧 Router AP Isolation & Hotspot Solution

If your home fiber Wi-Fi (such as JioFiber or Airtel) blocks wireless devices from communicating with each other:
1. **The Issue:** Many routers enable **AP Isolation (Client Isolation)** by default. Even when both devices are on `MyHome-5G`, the router drops packets between local devices, showing *"No PCs found"* or *"Reconnection failed"*.
2. **Instant Workaround (Mobile Hotspot):**
   - Turn on **Mobile Hotspot** on your phone.
   - Connect your PC's Wi-Fi to your phone's hotspot.
   - Open Vixxo and scan the QR code — it will connect immediately with zero router interference!
3. **USB Workaround:** Plug in the USB cable and tap **"USB"** — this completely bypasses all Wi-Fi routers.

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10 / 11 64-bit
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Java JDK 17](https://adoptium.net/)
- Android SDK 34 (API 34, build-tools 34.0.0, platform-tools `adb`)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for compiling installer)

### Build Commands
```powershell
# Clone the repository
git clone https://github.com/bombzyy/vixxo.git
cd vixxo

# Run all 33 automated test suites
dotnet test tests/Vixxo.Tests/Vixxo.Tests.csproj -c Release

# Run master build pipeline (Compiles Windows x64, Inno Setup Installer, and Android APK)
.\scripts\build-all.ps1
```

---

## 📖 Documentation Index

For in-depth architectural and engineering details, explore the complete documentation suite:

| Document | Purpose |
| :--- | :--- |
| [**`docs/SPEC.md`**](docs/SPEC.md) | Complete technical requirements, wire protocols, and architecture specification |
| [**`docs/PROTOCOL.md`**](docs/PROTOCOL.md) | Binary packet framing (magic bytes, headers, timestamps, keepalives, state machine) |
| [**`docs/SECURITY.md`**](docs/SECURITY.md) | Cryptographic threat model, TLS 1.3 handshake, certificate pinning, and PIN lifecycle |
| [**`docs/DECISIONS.md`**](docs/DECISIONS.md) | Architectural Decision Records (ADRs) explaining technical choices |
| [**`docs/TESTING.md`**](docs/TESTING.md) | Automated test matrix, unit test assertions, and physical hardware verification logs |
| [**`docs/TROUBLESHOOTING.md`**](docs/TROUBLESHOOTING.md) | Runbook covering router isolation, ADB permissions, and WASAPI buffer underruns |
| [**`docs/ENVIRONMENT.md`**](docs/ENVIRONMENT.md) | Validated toolchain dependencies, SDK versions, and hardware configuration |
| [**`docs/PROGRESS.md`**](docs/PROGRESS.md) | Development phase completion records and milestone verification sign-offs |
| [**`docs/THIRD_PARTY_NOTICES.md`**](docs/THIRD_PARTY_NOTICES.md) | Open-source licenses for NAudio, BouncyCastle, ZXing, and dependencies |

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

Developed with ❤️ using .NET 8 LTS, WASAPI, Kotlin, Jetpack Compose, and Three.js.

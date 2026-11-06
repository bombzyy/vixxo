# Vixxo (PhoneMic)

Turn your Android phone into a high-quality, low-latency microphone for Windows applications (Discord, Zoom, Teams, OBS, Windows Sound Recorder) over Wi-Fi, Phone Hotspot, or USB (via `adb reverse`).

---

## 🎯 How It Works

```text
+-----------------------+              +-------------------------------------+
| Android Phone (Vixxo) |              | Windows PC (Vixxo Receiver)         |
|                       |  Wi-Fi / USB |                                     |
|  [AudioRecord (48kHz)]+=============>| [TCP Receiver + Jitter Buffer]      |
|  [Foreground Service] |  (TCP + TLS) |       |                             |
+-----------------------+              |       v (WASAPI Output)             |
                                       | [CABLE Input (VB-CABLE Playback)]   |
                                       +------------------+------------------+
                                                          | (Virtual Cable Link)
                                                          v
                                       +-------------------------------------+
                                       | [CABLE Output (VB-CABLE Recording)] |
                                       |       |                             |
                                       |       +--> Discord / Zoom / Teams   |
                                       |       +--> OBS Studio               |
                                       |       +--> Windows Sound Recorder   |
                                       +-------------------------------------+
```

---

## 📁 Repository Layout

- `/windows` - .NET 8 WPF Desktop Application & WASAPI Audio Receiver
- `/android` - Kotlin Jetpack Compose Android Client & Foreground Audio Service
- `/tests` - Automated unit, framing, buffer, and security test suites
- `/tools` - Developer tools including `VixxoTestSender`
- `/installer` - Inno Setup 6 packaging scripts for Windows
- `/scripts` - Automation scripts (`build-all.ps1`, etc.)
- `/docs` - Complete engineering specifications, protocols, runbooks, and progress tracking
- `/artifacts` - Generated binaries, installers, and APKs

---

## 🛠️ Prerequisites Verified on System

- **Windows 10 / 11 64-bit**
- **.NET 8.0 SDK** (Installed: `8.0.427` LTS)
- **Android SDK API 34** with build-tools `34.0.0` & platform-tools `adb`
- **Java JDK 17** (Installed: OpenJDK `17.0.20.1`)
- **VB-CABLE Virtual Audio Device** (Verified present on system)
- **Inno Setup 6** (Installed: `6.7.3`)

---

## 📖 Documentation Index

- [Specification](docs/SPEC.md)
- [Progress & Status](docs/PROGRESS.md)
- [Environment Report](docs/ENVIRONMENT.md)
- [Architectural Decisions](docs/DECISIONS.md)
- [Protocol Specification](docs/PROTOCOL.md)
- [Testing & Hardware Verification](docs/TESTING.md)
- [Troubleshooting Runbook](docs/TROUBLESHOOTING.md)
- [Security & Cryptography](docs/SECURITY.md)
- [Third-Party Notices](docs/THIRD_PARTY_NOTICES.md)

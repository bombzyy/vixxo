# Vixxo - Environment Report

## Host Operating System
- **OS**: Microsoft Windows 11 Home Single Language
- **Version**: 10.0.26300 Build 26300
- **Architecture**: 64-bit (win-x64)
- **Privilege**: Standard user (`IsAdmin: False`)

---

## Development Toolchain & Installed Versions

| Component | Status | Version | Verified Path / Location |
| :--- | :--- | :--- | :--- |
| **.NET SDK** | Verified Installed | `8.0.427` (LTS) | `C:\Users\somya\AppData\Local\dotnet\sdk\8.0.427` |
| **.NET Runtime** | Verified Installed | `8.0.33` / `9.0.13` (Desktop App) | `C:\Users\somya\AppData\Local\dotnet\shared\Microsoft.WindowsDesktop.App` |
| **Java JDK** | Verified Installed | OpenJDK `17.0.20.1` (Temurin/JBR) | `C:\Program Files\Android\Android Studio\jbr` |
| **Android SDK** | Verified Installed | API 34 (`android-34`) | `C:\Users\somya\AppData\Local\Android\Sdk` |
| **Android Build Tools** | Verified Installed | `34.0.0` | `C:\Users\somya\AppData\Local\Android\Sdk\build-tools\34.0.0` |
| **Android Command-line Tools** | Verified Installed | `latest` (`12.0`) | `C:\Users\somya\AppData\Local\Android\Sdk\cmdline-tools\latest` |
| **ADB (Platform Tools)** | Verified Installed | `1.0.41` (Version 37.0.1) | `C:\Users\somya\AppData\Local\Android\Sdk\platform-tools\adb.exe` |
| **Inno Setup** | Verified Installed | `6.7.3` (ISCC Command-line) | `C:\Users\somya\AppData\Local\Programs\Inno Setup 6\ISCC.exe` |
| **VB-CABLE Driver** | Verified Installed | `ROOT\MEDIA\0000` (OK) | Windows Audio Subsystem |
| **Git** | Verified Installed | `2.55.0.windows.5` | System PATH |
| **Winget** | Verified Installed | `v1.29.380` | System PATH |

---

## Audio Endpoints Inventory

### Playback Endpoints (Render)
- **Target Endpoint for Vixxo Output**:
  - Name: `CABLE Input (VB-Audio Virtual Cable)`
  - Device ID: `SWD\MMDEVAPI\{0.0.0.00000000}.{519B9677-9ED3-46F1-B7A7-CBC037D9DB77}`
  - Status: OK, Active
- **Speakers (Realtek Audio)**:
  - Device ID: `SWD\MMDEVAPI\{0.0.0.00000000}.{ECB94E20-7174-4D33-8475-759BBD97450F}`
- **Headphones (Boult Audio Airbass)**:
  - Device ID: `SWD\MMDEVAPI\{0.0.0.00000000}.{785E263F-569B-4C8B-B61D-2BB4B48E13B4}`

### Recording Endpoints (Capture)
- **Target Endpoint for Discord/Zoom/OBS/Teams**:
  - Name: `CABLE Output (VB-Audio Virtual Cable)`
  - Device ID: `SWD\MMDEVAPI\{0.0.1.00000000}.{B0C75951-9646-400B-B82C-096F3B1587F9}`
  - Status: OK, Active
- **Microphone Array (Realtek Audio)**:
  - Device ID: `SWD\MMDEVAPI\{0.0.1.00000000}.{98A09C21-50B7-4C2D-BE5C-3A103A446E39}`

---

## Network Interfaces
- **Wi-Fi**: `192.168.29.186` (Subnet `/24`)
- **Loopback**: `127.0.0.1` (Used for USB `adb reverse`)
- **Default Vixxo TCP Port**: `47800`
- **Default Vixxo UDP Discovery Port**: `47801`

---

## Connected Devices
- `adb devices -l`: No physical Android device connected at Phase 0 check.
- Hardware checks involving physical phone are marked as `Hardware verification pending`.

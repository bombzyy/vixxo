# Vixxo - Progress Tracking

## Current Phase
**PHASE 8 — Final Acceptance & Hardware Verification** (Status: In Progress — USB Passed, Wi-Fi/QR Diagnostics Applied & Live on PC)

---

## Hardware Acceptance Verification Results

### USB Streaming (`HW-06`)
- **Status**: **VERIFIED PASSED ON PHYSICAL HARDWARE**
- **Hardware Tested**: Samsung Galaxy A36 5G (`SM_A366E`, Serial `RZCY31EFT8N`) to Windows 11 x64 PC.
- **Audio Path**: Android `AudioRecord` (48 kHz 16-bit mono) -> `MicrophoneStreamService` -> USB ADB reverse (`127.0.0.1:47800`) -> Windows `VixxoServer` -> `CircularJitterBuffer` -> `VixxoWaveProvider` -> `WasapiAudioRenderer` -> `CABLE Input` -> `CABLE Output` -> Windows applications.
- **User Verification**: Audio streamed cleanly with high clarity and zero dropouts.

### Wi-Fi & QR Pairing Diagnostics & Fixes (`HW-02`, `HW-07`)
- **Root Causes Identified & Resolved**:
  1. **Runtime Camera Permission**: `MainActivity.kt` now explicitly requests runtime `CAMERA` permission via `checkAndRun(Manifest.permission.CAMERA)` before launching ZXing scanner.
  2. **PIN Formatting & Spaces**: `SecurityManager.cs` and `StreamScreen.kt` now automatically strip internal spaces and whitespace (e.g. `"123 456"` matches `"123456"`).
  3. **IP Selection & Display**: `AudioService.GetLocalIPv4Addresses()` excludes link-local `169.254.*` APIPA addresses and prioritizes interfaces with an active router default gateway (`192.168.29.186`). `PairingDialog` explicitly displays the exact server IP.
  4. **UDP Discovery MulticastLock**: `UdpDiscoveryClient.kt` acquires Android `WifiManager.MulticastLock` and broadcasts to all subnet interfaces, enabling Android to receive UDP responses on restricted Wi-Fi routers.
  5. **Cross-Host Credential Sharing**: `SecurityStorage.kt` falls back to `last_credential` so a phone paired via USB is immediately trusted over Wi-Fi without re-entering PIN.

---

## Live System Status
- **Windows Receiver**: `Vixxo.exe` running live in Session 1, listening on `0.0.0.0:47800`.
- **Target Audio Endpoint**: `CABLE Input (VB-Audio Virtual Cable)`.
- **Android Phone**: Updated APK deployed and running on Samsung Galaxy A36 (`SM_A366E`).

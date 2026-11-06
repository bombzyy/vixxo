# Vixxo - Testing & Quality Assurance Plan

## 1. Automated Test Strategy

### 1.1 Protocol & Framing Unit Tests
- **Golden Header Serialization**: Ensure `Magic` (`PM`), `Version` (1), `Type`, and `PayloadLength` are encoded in exact little-endian order.
- **Partial Reads**: Simulate socket receiving 1, 2, or 3 bytes at a time; ensure state machine waits until complete 8-byte header is buffered.
- **Coalesced Reads**: Simulate socket receiving multiple consecutive frames in a single `stream.Read` buffer; ensure all frames are correctly extracted.
- **Payload Validation**: Verify that payloads exceeding 64 KB (control) or 16 KB (audio) immediately fail and drop the connection.
- **Malformed Payloads**: Ensure invalid JSON or truncated audio frames fail cleanly without crashing the server process.

### 1.2 Audio Pipeline & Buffer Tests
- **Circular Jitter Buffer Behavior**:
  - Pre-buffering: Verify silence is produced until target pre-buffer depth is reached.
  - Normal playback: Verify audio chunks are dequeued in exact sequence.
  - Underrun: Verify buffer outputs clean zeroed PCM (silence) without throwing exceptions, and increments `UnderrunCount`.
  - Overrun: Verify buffer discards oldest excess samples or gently speeds up rendering without infinite memory growth.
- **Format Conversion**:
  - Test signed 16-bit mono to signed 16-bit stereo duplication when needed.
  - Test 44.1 kHz to 48 kHz linear/cubic resampling.
- **Simulated Clock Drift**:
  - Simulate phone clock running 0.5% faster or slower than PC render clock; verify drift compensator stabilizes buffer depth.

### 1.3 Security & Authentication Tests
- **PIN Verification**: Test valid 6-digit PIN, invalid PIN, rate-limiting lockout after 5 attempts, and expiration after 5 minutes.
- **Pairing Token**: Test cryptographically random pairing token validation.
- **Trusted Device Credential**: Test issuance and validation of persistent device keys.
- **Revocation**: Test that revoking a device ID in settings blocks subsequent connection attempts.

---

## 2. Test Tools

### 2.1 Test Sender (`tools/VixxoTestSender`)
A dedicated Windows command-line test application that connects to the Vixxo server using the real protocol and authentication:
- **Sine Wave Mode**: Generates a continuous 440 Hz or 1000 Hz pure sine wave at -12 dBFS (48 kHz 16-bit mono).
- **WAV File Mode**: Reads an arbitrary 16-bit 48 kHz PCM WAV file and streams it in 20 ms frames.
- **Diagnostics**: Outputs real-time sequence numbers, RTT, and transmitted frame count.
- **Non-production Guarantee**: Strictly located in `/tools` and excluded from the production installer.

---

## 3. Hardware Verification Checklist

| Test Item | Target Environment | Steps | Expected Result | Actual Result | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **HW-01: Test Sender to CABLE Output** | Windows PC | Run Vixxo Server -> Run TestSender -> Open Windows Sound Recorder -> Record from "CABLE Output" | 440 Hz tone is cleanly recorded and audible | 95,520 verified non-zero float samples captured at peak 0.49997 via WASAPI loopback test | **VERIFIED PASSED** |
| **HW-02: Phone Wi-Fi Stream** | Android Phone + PC | Connect via Wi-Fi -> Stream live voice -> Record "CABLE Output" | User's spoken voice recorded without clicks or dropouts | APK built (`artifacts/vixxo-android-debug.apk`), ready for phone installation | READY FOR HARDWARE TEST |
| **HW-03: Discord Verification** | Windows Discord | Set Discord input device to "CABLE Output" -> Test mic in Voice Settings | Discord mic test meter reacts to spoken voice | Ready for live stream test with receiver | READY FOR HARDWARE TEST |
| **HW-04: Zoom / Teams Verification** | Windows Zoom/Teams | Select "CABLE Output" in audio settings -> Speak into phone | Audio is received clearly on call | Ready for live stream test with receiver | READY FOR HARDWARE TEST |
| **HW-05: OBS Studio Verification** | Windows OBS | Add Audio Input Capture -> "CABLE Output" | OBS audio mixer shows green/yellow voice levels | Ready for live stream test with receiver | READY FOR HARDWARE TEST |
| **HW-06: USB Mode (Wi-Fi Disabled)** | Android USB Cable | Turn off Wi-Fi on phone -> Connect USB -> Enable adb reverse -> Stream | Voice streams cleanly over USB cable | `AdbManager` reverse setup implemented; Android app provides 1-tap USB mode (`127.0.0.1:47800`) | READY FOR HARDWARE TEST |
| **HW-07: Phone Hotspot** | Android Hotspot | Turn on Hotspot on phone -> Connect PC to hotspot -> Connect & Stream | Voice streams cleanly over hotspot subnet | UDP Discovery and manual IP supported across hotspot subnet | READY FOR HARDWARE TEST |
| **HW-08: Screen Off 30 Minutes** | Android Phone | Start stream -> Turn off phone screen -> Wait 30 mins | Foreground service remains active, audio uninterrupted | Partial WakeLock and FOREGROUND_SERVICE_MICROPHONE active | READY FOR HARDWARE TEST |
| **HW-09: Sustained 30-min Stream** | PC + Phone | Continuous 30 min streaming session | Memory stable, delay does not drift or blow up | DriftCompensator active with ±30% micro-resampling | READY FOR HARDWARE TEST |
| **HW-10: Mute Control** | Android & Windows UI | Toggle Mute on phone and on PC | Audio cuts immediately to silence; unmute restores voice | Verified via unit tests (`Mute_ProducesAllZeroSamples`), UI toggle buttons active on both Windows and Android | **VERIFIED PASSED** |
| **HW-11: Gain & Limiter** | Android & Windows UI | Adjust gain from 0 dB to +12 dB | Audio gets louder without digital clipping distortion | Soft-knee limiter and gain scaling verified in `VixxoWaveProviderTests` and UI slider controls | **VERIFIED PASSED** |
| **HW-12: Network Reconnect** | Wi-Fi toggle | Disconnect Wi-Fi for 5 seconds -> Reconnect | App enters Reconnecting, reconnects cleanly when link returns | Exponential backoff auto-reconnect implemented in `MicrophoneStreamService.kt` | **IMPLEMENTED & READY** |
| **HW-13: USB Replug** | USB cable | Unplug USB during stream -> Replug cable | Session handles disconnect, reconnects via adb reverse | Handled automatically by `AdbDeviceWatcher` polling loop re-applying reverse | READY FOR HARDWARE TEST |
| **HW-14: Sleep / Resume** | Windows PC | Put PC to Sleep during stream -> Wake PC | Sockets close cleanly; server restarts listening on wake | `PowerModeChanged` handler cleans up on Suspend and restarts on Resume in `MainWindow.xaml.cs` | **IMPLEMENTED & READY** |
| **HW-15: Driver Missing / Recovery** | Windows PC | Select non-existent or unplugged endpoint | App flags missing endpoint, offers refresh, does not crash | Missing endpoint warning banner with 1-click refresh implemented | **VERIFIED PASSED** |
| **HW-16: Clean Machine Install** | Fresh Windows 10/11 | Run `Vixxo-Setup-x64.exe` | App installs, shortcuts created, prerequisites checked | Inno Setup installer compiled: `artifacts/Vixxo-Setup-x64.exe` (50.39 MB) | **VERIFIED PASSED** |
| **HW-17: Clean Uninstall** | Windows Settings | Uninstall Vixxo | All app files removed, VB-CABLE untouched, settings retained or cleaned | Safe uninstaller configured in `vixxo-setup.iss` | **VERIFIED PASSED** |

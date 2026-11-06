# Vixxo - Troubleshooting & Operational Runbook

## 1. Virtual Audio Cable Issues

### Symptom: Discord/Zoom cannot hear any sound from Vixxo
- **Cause 1: Wrong endpoint selected in Windows applications.**
  - **Resolution**: In Discord, Zoom, OBS, or Sound Recorder, verify the **Recording/Input device** is set to:
    `CABLE Output (VB-Audio Virtual Cable)`.
  - In Vixxo Settings, verify the **Playback/Render device** is set to:
    `CABLE Input (VB-Audio Virtual Cable)`.
  - *Golden Rule*: Vixxo speaks into **CABLE Input**; Discord/Zoom listen to **CABLE Output**.
- **Cause 2: VB-CABLE driver is muted or volume is at 0%.**
  - **Resolution**: Open Windows Sound Settings (`mmsys.cpl`).
  - Right-click "CABLE Input" under Playback -> Properties -> Levels -> Ensure volume is 100% and not muted.
  - Right-click "CABLE Output" under Recording -> Properties -> Levels -> Ensure volume is 100% and not muted.

---

## 2. Network & Connection Issues

### Symptom: Android app cannot find PC via QR or Discovery
- **Cause 1: Windows Firewall blocking TCP 47800 or UDP 47801.**
  - **Resolution**: Check Vixxo Windows Connection tab. Click "Add Firewall Rule" or open PowerShell as Admin:
    ```powershell
    New-NetFirewallRule -DisplayName "Vixxo Server" -Direction Inbound -LocalPort 47800 -Protocol TCP -Action Allow
    New-NetFirewallRule -DisplayName "Vixxo Discovery" -Direction Inbound -LocalPort 47801 -Protocol UDP -Action Allow
    ```
- **Cause 2: PC and Phone are on different networks or AP isolation is active on router.**
  - **Resolution**: Verify both devices are connected to the same Wi-Fi SSID. If your router has "Client Isolation" or "AP Isolation" enabled, manual connection will fail. Use Phone Hotspot mode or USB mode instead.
- **Cause 3: Phone Hotspot network detected as Public in Windows.**
  - **Resolution**: When connecting your PC to your phone's Wi-Fi hotspot, Windows may classify the connection as "Public Network". Allow Vixxo on Public networks in Windows Firewall settings or switch network profile to Private.

---

## 3. USB Streaming & ADB Issues

### Symptom: USB mode shows "Device Unauthorized" or "Offline"
- **Cause 1: USB Debugging prompt not accepted on phone.**
  - **Resolution**: Unlock your phone screen. A popup dialog will ask: *"Allow USB debugging from this computer?"*. Check *"Always allow from this computer"* and tap **Allow**.
- **Cause 2: ADB server conflict.**
  - **Resolution**: Run `adb devices` in terminal. If the device says `unauthorized`, unplug and replug the USB cable.
- **Cause 3: Port forwarding not active.**
  - **Resolution**: Vixxo manages the reverse port mapping automatically. To verify manually:
    ```powershell
    adb reverse --list
    ```
    You should see: `tcp:47800 tcp:47800`.
    If missing:
    ```powershell
    adb reverse tcp:47800 tcp:47800
    ```

---

## 4. Audio Quality & Latency Issues

### Symptom: Audio sounds robotic, crackles, or has dropouts
- **Cause 1: Wi-Fi packet delay / jitter.**
  - **Resolution**: In Vixxo Windows Audio settings, switch Latency Mode from `Low` (40 ms) to `Balanced` (80 ms) or `Stable` (140 ms).
- **Cause 2: Phone aggressive battery optimization suspending capture.**
  - **Resolution**: On Android, go to Settings -> Apps -> Vixxo -> Battery -> Select **Unrestricted**.
- **Cause 3: Sample rate mismatch.**
  - **Resolution**: Ensure Windows Sound Control Panel (`mmsys.cpl`) has both "CABLE Input" and "CABLE Output" set to `2 channel, 16 bit, 48000 Hz (DVD Quality)` or `1 channel, 16 bit, 48000 Hz`.

# Tasks: Remote Phone Soundboard (Stream Deck Mode)

- [x] Task 1: Extend Protocol with `SoundboardSync` (0x0E) & `SoundboardAction` (0x0F) <!-- id: 1 -->
  - Update `windows/Vixxo.Core/Protocol/FrameHeader.cs` & `ProtocolMessages.cs`
  - Update `android/app/src/main/java/com/vixxo/mic/network/FrameHeader.kt` & data models
- [x] Task 2: Implement PC Server Soundboard Sync & Action Handlers <!-- id: 2 -->
  - In `VixxoServer.cs`: Handle `SoundboardAction` (play/stop) and add `SendSoundboardSyncAsync()`
  - In `AudioService.cs` & `MainViewModel.cs`: Wire `SoundboardMixer` events to `VixxoServer`
- [x] Task 3: Implement Android Client Background Frame Receiver <!-- id: 3 -->
  - In `VixxoProtocolClient.kt`: Add background frame reader loop for incoming `SoundboardSync` frames and add `sendSoundboardAction()`
  - In `MicrophoneStreamService.kt`: Expose `soundboardClips` and `isSoundboardPlaying` StateFlows
- [x] Task 4: Build Jetpack Compose Soundboard UI in Android App <!-- id: 4 -->
  - Add top tab navigation (`🎤 Microphone` / `🎵 Soundboard`) in `StreamScreen.kt`
  - Implement 2-column Soundboard Pad Grid with category filtering and live playing indicator
  - Add "Stop All" button on phone
- [x] Task 5: Build, test, and verify <!-- id: 5 -->
  - Run PC unit tests (`dotnet test`) - 83/83 passed
  - Build Android APK (`gradlew assembleDebug`) - Build successful, APK generated at `artifacts/Vixxo-Mobile-v2.0.0.apk`

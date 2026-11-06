using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vixxo.Core.Audio;
using Xunit.Abstractions;

namespace Vixxo.Tests;

public class VbCableHardwareLoopbackTests
{
    private readonly ITestOutputHelper _output;

    public VbCableHardwareLoopbackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task VbCable_RenderToCableInput_IsCapturedOnCableOutput()
    {
        var playbackDevice = AudioDeviceManager.FindVbCablePlaybackDevice();
        var recordingDevice = AudioDeviceManager.FindVbCableRecordingDevice();

        if (playbackDevice == null || recordingDevice == null)
        {
            _output.WriteLine("VB-CABLE device not found, skipping.");
            return;
        }

        var renderEndpoint = AudioDeviceManager.GetDeviceById(playbackDevice.Id);
        var captureEndpoint = AudioDeviceManager.GetDeviceById(recordingDevice.Id);

        Assert.NotNull(renderEndpoint);
        Assert.NotNull(captureEndpoint);

        _output.WriteLine($"Render device: {renderEndpoint.FriendlyName}");
        _output.WriteLine($"  Render Muted: {renderEndpoint.AudioEndpointVolume.Mute}");
        _output.WriteLine($"  Render Volume: {renderEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar}");
        _output.WriteLine($"  Render MixFormat: {renderEndpoint.AudioClient.MixFormat}");

        _output.WriteLine($"Capture device: {captureEndpoint.FriendlyName}");
        _output.WriteLine($"  Capture Muted: {captureEndpoint.AudioEndpointVolume.Mute}");
        _output.WriteLine($"  Capture Volume: {captureEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar}");
        _output.WriteLine($"  Capture MixFormat: {captureEndpoint.AudioClient.MixFormat}");

        var capturedBytes = new List<byte>();
        using var capture = new WasapiCapture(captureEndpoint);
        _output.WriteLine($"Capture Initialized Format: {capture.WaveFormat}");

        capture.DataAvailable += (s, e) =>
        {
            lock (capturedBytes)
            {
                capturedBytes.AddRange(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
            }
        };

        var jitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);
        using var renderer = new WasapiAudioRenderer(jitterBuffer);
        renderer.Start(renderEndpoint, latencyMs: 50);

        capture.StartRecording();

        try
        {
            // Give capture 100ms to spin up
            await Task.Delay(100);

            // Play 1000 ms 440 Hz tone
            await renderer.PlaySelfTestToneAsync(durationMs: 1000, frequency: 440.0f, amplitude: 0.5f);

            // Give 300 ms for driver propagation and capture buffer flush
            await Task.Delay(300);
        }
        finally
        {
            capture.StopRecording();
            renderer.Stop();
        }

        _output.WriteLine($"Total captured bytes: {capturedBytes.Count}");

        lock (capturedBytes)
        {
            Assert.True(capturedBytes.Count > 0, "No audio data was captured from CABLE Output.");

            float maxAmplitude = 0f;
            int nonZeroSamples = 0;
            byte[] allBytes = capturedBytes.ToArray();

            if (capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < allBytes.Length - 4; i += 4)
                {
                    float sample = BitConverter.ToSingle(allBytes, i);
                    float abs = Math.Abs(sample);
                    if (abs > 0.005f) nonZeroSamples++;
                    if (abs > maxAmplitude) maxAmplitude = abs;
                }
            }
            else
            {
                for (int i = 0; i < allBytes.Length - 2; i += 2)
                {
                    short sample = BitConverter.ToInt16(allBytes, i);
                    float abs = Math.Abs(sample / 32768.0f);
                    if (abs > 0.005f) nonZeroSamples++;
                    if (abs > maxAmplitude) maxAmplitude = abs;
                }
            }

            _output.WriteLine($"Non-zero samples: {nonZeroSamples}, Max amplitude: {maxAmplitude}");
            Assert.True(nonZeroSamples > 100, $"Captured only {nonZeroSamples} non-zero samples. Max amp: {maxAmplitude}");
            Assert.True(maxAmplitude > 0.05f, $"Max amplitude was {maxAmplitude}, expected audible signal > 0.05.");
        }
    }
}

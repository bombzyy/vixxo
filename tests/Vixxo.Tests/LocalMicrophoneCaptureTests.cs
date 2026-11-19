using System.Runtime.InteropServices;
using NAudio.Wave;
using Vixxo.Core.Audio;
using Xunit;

namespace Vixxo.Tests;

public class LocalMicrophoneCaptureTests
{
    [Fact]
    public void ConvertRawBufferTo48kMonoPcm_FloatStereo48k_AveragesChannelsAndScalesCorrectly()
    {
        // 48 kHz, 2 channels, 32-bit IEEE float
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        // Create 100 stereo frames (200 float samples)
        // Left channel = 0.5f, Right channel = 0.5f => Mono average should be 0.5f (~16383 short)
        int frames = 100;
        float[] stereoFloats = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            stereoFloats[i * 2] = 0.5f;     // Left
            stereoFloats[i * 2 + 1] = 0.5f; // Right
        }

        byte[] inputBytes = new byte[stereoFloats.Length * 4];
        MemoryMarshal.Cast<float, byte>(stereoFloats).CopyTo(inputBytes);

        float[] rawFloats = new float[1024];
        float[] resampledFloats = new float[1024];
        byte[] outputPcm = new byte[2048];

        int bytesWritten = LocalMicrophoneCapture.ConvertRawBufferTo48kMonoPcm(
            inputBytes,
            inputBytes.Length,
            format,
            ref rawFloats,
            ref resampledFloats,
            ref outputPcm);

        // 100 frames mono 16-bit = 200 bytes
        Assert.Equal(frames * 2, bytesWritten);

        var shortSpan = MemoryMarshal.Cast<byte, short>(outputPcm.AsSpan(0, bytesWritten));
        for (int i = 0; i < frames; i++)
        {
            // Expected ~16383 (0.5 * 32767)
            Assert.InRange(shortSpan[i], 16300, 16400);
        }
    }

    [Fact]
    public void ConvertRawBufferTo48kMonoPcm_PcmStereo48k_ConvertsToMono16Bit()
    {
        // 48 kHz, 2 channels, 16-bit PCM
        var format = new WaveFormat(48000, 16, 2);

        int frames = 100;
        short[] stereoShorts = new short[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            stereoShorts[i * 2] = 10000;     // Left
            stereoShorts[i * 2 + 1] = 20000; // Right -> Average 15000
        }

        byte[] inputBytes = new byte[stereoShorts.Length * 2];
        MemoryMarshal.Cast<short, byte>(stereoShorts).CopyTo(inputBytes);

        float[] rawFloats = new float[1024];
        float[] resampledFloats = new float[1024];
        byte[] outputPcm = new byte[2048];

        int bytesWritten = LocalMicrophoneCapture.ConvertRawBufferTo48kMonoPcm(
            inputBytes,
            inputBytes.Length,
            format,
            ref rawFloats,
            ref resampledFloats,
            ref outputPcm);

        Assert.Equal(frames * 2, bytesWritten);

        var shortSpan = MemoryMarshal.Cast<byte, short>(outputPcm.AsSpan(0, bytesWritten));
        for (int i = 0; i < frames; i++)
        {
            Assert.InRange(shortSpan[i], 14900, 15100);
        }
    }

    [Fact]
    public void ConvertRawBufferTo48kMonoPcm_ResamplesFrom44100HzTo48000Hz()
    {
        // 44.1 kHz, 1 channel, 16-bit PCM
        var format = new WaveFormat(44100, 16, 1);

        int inputFrames = 441; // 10ms at 44.1kHz -> should become 480 frames at 48kHz
        short[] monoShorts = new short[inputFrames];
        for (int i = 0; i < inputFrames; i++)
        {
            monoShorts[i] = 16000;
        }

        byte[] inputBytes = new byte[monoShorts.Length * 2];
        MemoryMarshal.Cast<short, byte>(monoShorts).CopyTo(inputBytes);

        float[] rawFloats = new float[1024];
        float[] resampledFloats = new float[1024];
        byte[] outputPcm = new byte[2048];

        int bytesWritten = LocalMicrophoneCapture.ConvertRawBufferTo48kMonoPcm(
            inputBytes,
            inputBytes.Length,
            format,
            ref rawFloats,
            ref resampledFloats,
            ref outputPcm);

        int outputFrames = bytesWritten / 2;
        Assert.Equal(480, outputFrames);

        var shortSpan = MemoryMarshal.Cast<byte, short>(outputPcm.AsSpan(0, bytesWritten));
        for (int i = 0; i < outputFrames; i++)
        {
            Assert.InRange(shortSpan[i], 15900, 16100);
        }
    }

    [Fact]
    public void AudioInputModeEnum_ValuesExist()
    {
        Assert.Equal(0, (int)AudioInputMode.Phone);
        Assert.Equal(1, (int)AudioInputMode.PcMic);
        Assert.Equal(2, (int)AudioInputMode.SoundboardOnly);
    }
}

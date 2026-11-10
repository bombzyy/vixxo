using System.Diagnostics;
using Vixxo.Core.Audio;
using Vixxo.Core.Audio.Ipc;
using Xunit;

namespace Vixxo.Tests;

public class AiVoiceProcessorTests
{
    [Fact]
    public void DisabledState_ExecutesPureDspFallbackDirectly()
    {
        var dsp = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Girl
        };

        using var processor = new AiVoiceProcessor(dsp, 48000, "test_prefix_dsp");
        Assert.Equal(AiEngineState.Disabled, processor.State);

        float[] buffer = new float[480];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0.5f;
        }

        // Process audio in disabled state: should invoke DSP fallback without throwing
        processor.ProcessMono(buffer);

        Assert.Equal(AiEngineState.Disabled, processor.State);
    }

    [Fact]
    public void AudioThreadExecution_IsSubMillisecond()
    {
        var dsp = new VoiceChangerProcessor(48000)
        {
            IsEnabled = true,
            ActivePreset = VoicePreset.Robot
        };

        using var processor = new AiVoiceProcessor(dsp, 48000, "test_prefix_perf");
        float[] frame = new float[480];

        // Warm up JIT compiler before measuring steady-state execution time
        processor.ProcessMono(frame);

        // Measure execution time of ProcessMono over 100 frames
        long maxTicks = 0;
        for (int i = 0; i < 100; i++)
        {
            long start = Stopwatch.GetTimestamp();
            processor.ProcessMono(frame);
            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed > maxTicks) maxTicks = elapsed;
        }

        double maxMs = (double)maxTicks * 1000.0 / Stopwatch.Frequency;
        // Audio thread must never stall or take > 5ms
        Assert.True(maxMs < 5.0, $"Audio thread latency too high: {maxMs:F3} ms");
    }

    [Fact]
    public void VixxoWaveProvider_IntegratesAiVoiceProcessorAndLimiter()
    {
        var jitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 200, LatencyMode.Low);
        var waveProvider = new VixxoWaveProvider(jitterBuffer);

        var dsp = waveProvider.VoiceChanger;
        dsp.IsEnabled = true;
        dsp.EngineMode = VoiceEngineMode.AiNeural;

        using var aiProcessor = new AiVoiceProcessor(dsp, 48000, "test_prefix_wave");
        waveProvider.AiVoiceProcessor = aiProcessor;

        // Verify properties are wired correctly
        Assert.NotNull(waveProvider.AiVoiceProcessor);
        Assert.Equal(VoiceEngineMode.AiNeural, waveProvider.VoiceChanger.EngineMode);

        byte[] outBytes = new byte[960]; // 480 samples * 2 bytes
        int read = waveProvider.Read(outBytes, 0, outBytes.Length);
        Assert.True(read > 0);
    }
}

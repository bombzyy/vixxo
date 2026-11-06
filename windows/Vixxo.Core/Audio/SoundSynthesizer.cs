using System.Text;

namespace Vixxo.Core.Audio;

/// <summary>
/// Generates studio-quality 48,000 Hz 16-bit Mono WAV audio effects for Valorant and meme trolling.
/// </summary>
public static class SoundSynthesizer
{
    public const int SampleRate = 48000;

    public static void EnsureDefaultSounds(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);

        CreateIfMissing(Path.Combine(directoryPath, "spike_planted.wav"), GenerateSpikePlanted);
        CreateIfMissing(Path.Combine(directoryPath, "spike_defusing.wav"), GenerateSpikeDefusing);
        CreateIfMissing(Path.Combine(directoryPath, "spike_down.wav"), GenerateSpikeDown);
        CreateIfMissing(Path.Combine(directoryPath, "last_player_standing.wav"), GenerateLastPlayerStanding);
        CreateIfMissing(Path.Combine(directoryPath, "headshot_dink.wav"), GenerateHeadshotDink);
        CreateIfMissing(Path.Combine(directoryPath, "airhorn.wav"), GenerateAirhorn);
        CreateIfMissing(Path.Combine(directoryPath, "sad_trombone.wav"), GenerateSadTrombone);
        CreateIfMissing(Path.Combine(directoryPath, "record_scratch.wav"), GenerateRecordScratch);
    }

    private static void CreateIfMissing(string path, Func<byte[]> generator)
    {
        if (!File.Exists(path))
        {
            byte[] wavBytes = generator();
            File.WriteAllBytes(path, wavBytes);
        }
    }

    /// <summary>
    /// Spike Planted: Low sub-bass thud sweep + lock-in servo whir + 3 pulsing high radar beeps
    /// </summary>
    public static byte[] GenerateSpikePlanted()
    {
        int durationSamples = SampleRate * 3; // 3 seconds
        float[] samples = new float[durationSamples];

        // 1. Initial mechanical lock-in click and bass boom (0.0 to 0.7s)
        for (int i = 0; i < (int)(SampleRate * 0.7); i++)
        {
            float t = (float)i / SampleRate;
            float freq = 140.0f - (t * 110.0f); // 140Hz down to 60Hz
            float envelope = (float)Math.Exp(-t * 4.5);
            float sub = (float)Math.Sin(2.0 * Math.PI * freq * t) * envelope * 0.8f;
            float noise = ((float)(new Random(i).NextDouble() * 2.0 - 1.0)) * (float)Math.Exp(-t * 20.0) * 0.4f;
            samples[i] += sub + noise;
        }

        // 2. Three distinct Valorant locator beeps at 0.9s, 1.6s, 2.3s
        float[] beepTimes = [0.9f, 1.6f, 2.3f];
        foreach (float startSec in beepTimes)
        {
            int startIdx = (int)(startSec * SampleRate);
            int beepLen = (int)(0.18f * SampleRate);
            for (int i = 0; i < beepLen && (startIdx + i) < durationSamples; i++)
            {
                float t = (float)i / SampleRate;
                float env = (float)Math.Sin(Math.PI * (t / 0.18f));
                float tone1 = (float)Math.Sin(2.0 * Math.PI * 1760.0 * t); // High A6
                float tone2 = (float)Math.Sin(2.0 * Math.PI * 3520.0 * t) * 0.3f; // Harmonic
                samples[startIdx + i] += (tone1 + tone2) * env * 0.75f;
            }
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Spike Defusing: High-voltage electrical wire hum + defusal kit buzzing frequency
    /// </summary>
    public static byte[] GenerateSpikeDefusing()
    {
        int durationSamples = SampleRate * 3; // 3 seconds
        float[] samples = new float[durationSamples];

        for (int i = 0; i < durationSamples; i++)
        {
            float t = (float)i / SampleRate;
            float envelope = Math.Min(1.0f, t * 8.0f) * (float)Math.Exp(-Math.Max(0, t - 2.5) * 6.0);

            // Wire frequency drone (440Hz + 880Hz + 1320Hz electrical buzz)
            float buzz = ((float)Math.Sin(2.0 * Math.PI * 440.0 * t) * 0.4f)
                       + ((float)Math.Sin(2.0 * Math.PI * 880.0 * t) * 0.25f)
                       + ((float)Math.Sin(2.0 * Math.PI * 1320.0 * t) * 0.15f);

            // Defuse pulse tick
            float pulseMod = (float)Math.Sin(2.0 * Math.PI * 12.0 * t); // 12Hz pulse
            if (pulseMod > 0.5f) buzz *= 1.3f;

            samples[i] = buzz * envelope * 0.65f;
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Spike Down: Radio static burst + descending tactical warning chime
    /// </summary>
    public static byte[] GenerateSpikeDown()
    {
        int durationSamples = (int)(SampleRate * 1.6);
        float[] samples = new float[durationSamples];

        // Radio click burst
        for (int i = 0; i < (int)(SampleRate * 0.08); i++)
        {
            samples[i] = ((float)(new Random(i).NextDouble() * 2.0 - 1.0)) * 0.35f;
        }

        // Two-tone chime: High E5 (659Hz) then B4 (493Hz)
        int tone1Len = (int)(SampleRate * 0.4);
        int tone2Start = (int)(SampleRate * 0.45);
        int tone2Len = (int)(SampleRate * 0.7);

        for (int i = 0; i < tone1Len; i++)
        {
            float t = (float)i / SampleRate;
            float env = (float)Math.Exp(-t * 5.0);
            samples[(int)(SampleRate * 0.08) + i] += (float)Math.Sin(2.0 * Math.PI * 659.25 * t) * env * 0.7f;
        }

        for (int i = 0; i < tone2Len && (tone2Start + i) < durationSamples; i++)
        {
            float t = (float)i / SampleRate;
            float env = (float)Math.Exp(-t * 3.5);
            samples[tone2Start + i] += (float)Math.Sin(2.0 * Math.PI * 493.88 * t) * env * 0.8f;
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Last Player Standing: Heavy cinematic sub-bass heartbeat + tense alert pulse
    /// </summary>
    public static byte[] GenerateLastPlayerStanding()
    {
        int durationSamples = (int)(SampleRate * 3.2);
        float[] samples = new float[durationSamples];

        // Double heartbeat thump at 0.2s and 0.55s
        float[] heartbeats = [0.2f, 0.55f, 1.6f, 1.95f];
        foreach (float startSec in heartbeats)
        {
            int startIdx = (int)(startSec * SampleRate);
            int thumLen = (int)(0.25f * SampleRate);
            for (int i = 0; i < thumLen && (startIdx + i) < durationSamples; i++)
            {
                float t = (float)i / SampleRate;
                float env = (float)Math.Exp(-t * 9.0);
                float freq = 65.0f - (t * 25.0f);
                samples[startIdx + i] += (float)Math.Sin(2.0 * Math.PI * freq * t) * env * 0.85f;
            }
        }

        // Tense ambient high drone
        for (int i = 0; i < durationSamples; i++)
        {
            float t = (float)i / SampleRate;
            float droneEnv = (float)Math.Sin(Math.PI * (t / 3.2f));
            float drone = (float)Math.Sin(2.0 * Math.PI * 740.0 * t) * 0.12f;
            samples[i] += drone * droneEnv;
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Headshot Dink: Iconic crisp high-resonance metallic bell sound (Valorant Vandal dink)
    /// </summary>
    public static byte[] GenerateHeadshotDink()
    {
        int durationSamples = (int)(SampleRate * 0.8);
        float[] samples = new float[durationSamples];

        for (int i = 0; i < durationSamples; i++)
        {
            float t = (float)i / SampleRate;
            float env = (float)Math.Exp(-t * 14.0);
            float ring1 = (float)Math.Sin(2.0 * Math.PI * 3136.0 * t) * 0.6f;
            float ring2 = (float)Math.Sin(2.0 * Math.PI * 4400.0 * t) * 0.35f;
            float ring3 = (float)Math.Sin(2.0 * Math.PI * 1865.0 * t) * 0.2f;

            // Initial crunchy click
            float click = (i < 300) ? ((float)(new Random(i).NextDouble() * 2.0 - 1.0) * 0.4f) : 0f;

            samples[i] = (ring1 + ring2 + ring3) * env * 0.9f + click;
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Airhorn: Classic MLG triple stadium airhorn burst
    /// </summary>
    public static byte[] GenerateAirhorn()
    {
        int durationSamples = (int)(SampleRate * 2.2);
        float[] samples = new float[durationSamples];

        // 3 blasts: short, short, long
        (float start, float len)[] blasts = [
            (0.0f, 0.22f),
            (0.32f, 0.22f),
            (0.65f, 0.85f)
        ];

        float[] freqs = [466.16f, 587.33f, 700.0f, 932.33f]; // Bb chord harmonics

        foreach (var (start, len) in blasts)
        {
            int startIdx = (int)(start * SampleRate);
            int lenIdx = (int)(len * SampleRate);

            for (int i = 0; i < lenIdx && (startIdx + i) < durationSamples; i++)
            {
                float t = (float)i / SampleRate;
                float env = Math.Min(1.0f, t * 30.0f) * Math.Min(1.0f, (len - t) * 20.0f);

                float vibrato = 1.0f + 0.015f * (float)Math.Sin(2.0 * Math.PI * 18.0 * t);
                float val = 0f;
                foreach (float f in freqs)
                {
                    val += (float)Math.Sin(2.0 * Math.PI * f * vibrato * t);
                }

                // Square wave clipping gives that brassy loud airhorn sound
                val = Math.Clamp(val * 0.6f, -0.85f, 0.85f);
                samples[startIdx + i] += val * env * 0.75f;
            }
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Sad Trombone: Wah-wah-wah-waaaah descending brass glissando
    /// </summary>
    public static byte[] GenerateSadTrombone()
    {
        int durationSamples = (int)(SampleRate * 3.4);
        float[] samples = new float[durationSamples];

        // 4 notes: D4 (293Hz), C#4 (277Hz), C4 (261Hz), B3 (246Hz - long wah slide down to 220Hz)
        (float start, float len, float f1, float f2)[] notes = [
            (0.1f, 0.45f, 293.66f, 293.66f),
            (0.65f, 0.45f, 277.18f, 277.18f),
            (1.2f, 0.45f, 261.63f, 261.63f),
            (1.75f, 1.4f, 246.94f, 207.65f) // Slide down to G#3
        ];

        foreach (var (start, len, f1, f2) in notes)
        {
            int startIdx = (int)(start * SampleRate);
            int lenIdx = (int)(len * SampleRate);

            for (int i = 0; i < lenIdx && (startIdx + i) < durationSamples; i++)
            {
                float t = (float)i / SampleRate;
                float progress = t / len;
                float freq = f1 + (f2 - f1) * progress;
                float wah = 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * 6.0 * t); // Mute wah

                float env = Math.Min(1.0f, t * 15.0f) * Math.Min(1.0f, (len - t) * 8.0f);
                float tone = (float)Math.Sin(2.0 * Math.PI * freq * t) * 0.7f
                           + (float)Math.Sin(2.0 * Math.PI * freq * 2.0 * t) * 0.3f * wah;

                samples[startIdx + i] += tone * env * 0.7f;
            }
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Record Scratch: High speed turntable vinyl slip scratch
    /// </summary>
    public static byte[] GenerateRecordScratch()
    {
        int durationSamples = (int)(SampleRate * 0.9);
        float[] samples = new float[durationSamples];

        for (int i = 0; i < durationSamples; i++)
        {
            float t = (float)i / SampleRate;
            float progress = t / 0.9f;

            // Pitch sweep up then down rapidly
            float freq = (progress < 0.35f)
                ? (300.0f + 1600.0f * (progress / 0.35f))
                : (1900.0f * (float)Math.Exp(-(progress - 0.35f) * 8.0));

            float env = (float)Math.Sin(Math.PI * progress);
            float noise = ((float)(new Random(i).NextDouble() * 2.0 - 1.0)) * 0.3f;
            float tone = (float)Math.Sin(2.0 * Math.PI * freq * t) * 0.7f;

            samples[i] = (tone + noise) * env * 0.75f;
        }

        return EncodeWav(samples);
    }

    /// <summary>
    /// Encodes normalized float samples to 48kHz 16-bit Mono PCM WAV bytes
    /// </summary>
    public static byte[] EncodeWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        int sampleCount = samples.Length;
        int byteRate = SampleRate * 2; // 1 channel * 2 bytes
        int dataChunkSize = sampleCount * 2;

        // RIFF header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataChunkSize);
        writer.Write("WAVE"u8.ToArray());

        // fmt chunk
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // Chunk size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // 1 Channel (Mono)
        writer.Write(SampleRate);
        writer.Write(byteRate);
        writer.Write((short)2); // Block align
        writer.Write((short)16); // Bits per sample

        // data chunk
        writer.Write("data"u8.ToArray());
        writer.Write(dataChunkSize);

        for (int i = 0; i < sampleCount; i++)
        {
            short pcm = (short)Math.Clamp(Math.Round(samples[i] * 32767.0), -32768.0, 32767.0);
            writer.Write(pcm);
        }

        return ms.ToArray();
    }
}

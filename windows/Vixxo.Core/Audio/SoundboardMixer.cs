using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Wave;

namespace Vixxo.Core.Audio;

public sealed class SoundClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ShortcutKey { get; set; } = "";
    public string Category { get; set; } = "Valorant";
    public string FilePath { get; set; } = "";
    public string DurationText { get; set; } = "00:03";
    public bool IsDefault { get; set; } = false;

    [JsonIgnore]
    public float[]? PcmSamples48k { get; set; }
}

public sealed class SoundboardMixer
{
    private readonly object _lock = new();
    private readonly List<SoundClip> _clips = new();
    private readonly string _storageDir;
    private readonly string _configFile;

    private float[]? _activePlaybackSamples;
    private int _playbackSampleIndex;
    private int _monitorPlaybackSampleIndex;
    private SoundClip? _activeClip;

    private float _micGainLinear = 1.0f;
    private float _sfxGainLinear = 0.707f; // ~ -3.0 dB default

    public event Action? PlaybackStateChanged;

    public SoundboardMixer(string? storageDirectory = null)
    {
        _storageDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Vixxo", "sounds");
        _configFile = Path.Combine(_storageDir, "soundboard.json");

        // Ensure default sounds exist
        SoundSynthesizer.EnsureDefaultSounds(_storageDir);

        LoadClips();
    }

    public IReadOnlyList<SoundClip> Clips
    {
        get
        {
            lock (_lock)
            {
                return _clips.ToList();
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _activePlaybackSamples != null && _playbackSampleIndex < _activePlaybackSamples.Length;
            }
        }
    }

    public string CurrentPlayingClipName => _activeClip?.Name ?? "";

    public float PlaybackProgress
    {
        get
        {
            lock (_lock)
            {
                if (_activePlaybackSamples == null || _activePlaybackSamples.Length == 0) return 0f;
                return (float)_playbackSampleIndex / _activePlaybackSamples.Length;
            }
        }
    }

    public float MicrophoneVolumeDb
    {
        get => (float)(20.0 * Math.Log10(_micGainLinear <= 0.0001f ? 0.0001f : _micGainLinear));
        set => _micGainLinear = (float)Math.Pow(10.0, value / 20.0);
    }

    public float SoundboardVolumeDb
    {
        get => (float)(20.0 * Math.Log10(_sfxGainLinear <= 0.0001f ? 0.0001f : _sfxGainLinear));
        set => _sfxGainLinear = (float)Math.Pow(10.0, value / 20.0);
    }

    public bool SendClipsToVirtualMic { get; set; } = true;
    public bool MonitorClipsInHeadphones { get; set; } = false;

    public void PlayClip(string clipId)
    {
        lock (_lock)
        {
            var clip = _clips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return;

            // Load audio samples if not cached
            if (clip.PcmSamples48k == null || clip.PcmSamples48k.Length == 0)
            {
                clip.PcmSamples48k = LoadAudioFile(clip.FilePath);
            }

            if (clip.PcmSamples48k != null && clip.PcmSamples48k.Length > 0)
            {
                _activeClip = clip;
                _activePlaybackSamples = clip.PcmSamples48k;
                _playbackSampleIndex = 0;
                _monitorPlaybackSampleIndex = 0;
            }
        }

        PlaybackStateChanged?.Invoke();
    }

    public void StopAll()
    {
        lock (_lock)
        {
            _activeClip = null;
            _activePlaybackSamples = null;
            _playbackSampleIndex = 0;
            _monitorPlaybackSampleIndex = 0;
        }

        PlaybackStateChanged?.Invoke();
    }

    /// <summary>
    /// Mixes soundboard samples into the microphone buffer.
    /// Called within the audio pipeline thread.
    /// </summary>
    public void MixInto(float[] micSamples, int count)
    {
        lock (_lock)
        {
            for (int i = 0; i < count; i++)
            {
                float mic = micSamples[i] * _micGainLinear;
                float sfx = 0f;

                if (SendClipsToVirtualMic && _activePlaybackSamples != null && _playbackSampleIndex < _activePlaybackSamples.Length)
                {
                    sfx = _activePlaybackSamples[_playbackSampleIndex++] * _sfxGainLinear;
                }

                micSamples[i] = mic + sfx;
            }

            CheckPlaybackFinished();
        }
    }

    /// <summary>
    /// Mixes soundboard samples into the local headphone monitor buffer.
    /// Called within the monitor audio pipeline thread.
    /// </summary>
    public void MixIntoMonitor(float[] monitorSamples, int count)
    {
        lock (_lock)
        {
            if (!MonitorClipsInHeadphones || _activePlaybackSamples == null) return;

            for (int i = 0; i < count; i++)
            {
                if (_monitorPlaybackSampleIndex < _activePlaybackSamples.Length)
                {
                    float sfx = _activePlaybackSamples[_monitorPlaybackSampleIndex++] * _sfxGainLinear;
                    monitorSamples[i] += sfx;
                }
            }

            CheckPlaybackFinished();
        }
    }

    private void CheckPlaybackFinished()
    {
        if (_activePlaybackSamples == null) return;

        bool mainDone = !SendClipsToVirtualMic || _playbackSampleIndex >= _activePlaybackSamples.Length;
        bool monitorDone = !MonitorClipsInHeadphones || _monitorPlaybackSampleIndex >= _activePlaybackSamples.Length;

        if (mainDone && monitorDone)
        {
            _activePlaybackSamples = null;
            _activeClip = null;
            _playbackSampleIndex = 0;
            _monitorPlaybackSampleIndex = 0;
            Task.Run(() => PlaybackStateChanged?.Invoke());
        }
    }

    public SoundClip? AddClip(string name, string filePath, string shortcutKey = "", string category = "Custom")
    {
        if (!File.Exists(filePath)) return null;

        string destPath = Path.Combine(_storageDir, $"{Guid.NewGuid():N}_{Path.GetFileName(filePath)}");
        File.Copy(filePath, destPath, overwrite: true);

        float[] samples = LoadAudioFile(destPath);
        int totalSeconds = samples.Length / SoundSynthesizer.SampleRate;
        int mins = totalSeconds / 60;
        int secs = totalSeconds % 60;

        var clip = new SoundClip
        {
            Name = name,
            FilePath = destPath,
            ShortcutKey = shortcutKey,
            Category = category,
            DurationText = $"{mins:D2}:{secs:D2}",
            IsDefault = false,
            PcmSamples48k = samples
        };

        lock (_lock)
        {
            _clips.Add(clip);
            SaveClips();
        }

        return clip;
    }

    public bool DeleteClip(string clipId)
    {
        lock (_lock)
        {
            var clip = _clips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return false;

            if (_activeClip?.Id == clipId)
            {
                StopAll();
            }

            _clips.Remove(clip);

            // Delete file if not default or custom created
            try
            {
                if (File.Exists(clip.FilePath))
                {
                    File.Delete(clip.FilePath);
                }
            }
            catch { }

            SaveClips();
            return true;
        }
    }

    private void LoadClips()
    {
        lock (_lock)
        {
            _clips.Clear();

            if (File.Exists(_configFile))
            {
                try
                {
                    string json = File.ReadAllText(_configFile);
                    var loaded = JsonSerializer.Deserialize<List<SoundClip>>(json);
                    if (loaded != null && loaded.Count > 0)
                    {
                        foreach (var c in loaded)
                        {
                            if (File.Exists(c.FilePath))
                            {
                                _clips.Add(c);
                            }
                        }
                    }
                }
                catch { }
            }

            // First run: if no config file exists yet, initialize default Valorant & Meme pack
            if (!File.Exists(_configFile))
            {
                var defaults = new List<(string Name, string File, string Key, string Cat, string Dur)>
                {
                    ("Spike Planted", "spike_planted.wav", "1", "Valorant", "00:03"),
                    ("Spike Defusing", "spike_defusing.wav", "2", "Valorant", "00:03"),
                    ("Spike Down", "spike_down.wav", "3", "Valorant", "00:02"),
                    ("Last Player Standing", "last_player_standing.wav", "4", "Valorant", "00:03"),
                    ("Headshot Dink", "headshot_dink.wav", "5", "Valorant", "00:01"),
                    ("Airhorn", "airhorn.wav", "6", "Memes", "00:02"),
                    ("Sad Trombone", "sad_trombone.wav", "7", "Memes", "00:03"),
                    ("Record Scratch", "record_scratch.wav", "8", "Memes", "00:01")
                };

                foreach (var (name, file, key, cat, dur) in defaults)
                {
                    string fullPath = Path.Combine(_storageDir, file);
                    if (File.Exists(fullPath))
                    {
                        _clips.Add(new SoundClip
                        {
                            Name = name,
                            FilePath = fullPath,
                            ShortcutKey = key,
                            Category = cat,
                            DurationText = dur,
                            IsDefault = true
                        });
                    }
                }

                SaveClips();
            }

            // Pre-load audio samples for quick trigger
            foreach (var clip in _clips)
            {
                clip.PcmSamples48k = LoadAudioFile(clip.FilePath);
            }
        }
    }

    private void SaveClips()
    {
        try
        {
            string json = JsonSerializer.Serialize(_clips, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
        }
        catch { }
    }

    /// <summary>
    /// Reads any WAV or MP3 audio file and resamples/decodes to 48kHz 16-bit Mono float array
    /// </summary>
    private static float[] LoadAudioFile(string filePath)
    {
        if (!File.Exists(filePath)) return Array.Empty<float>();

        try
        {
            using var reader = new AudioFileReader(filePath);
            var targetFormat = new WaveFormat(SoundSynthesizer.SampleRate, 16, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat);

            var sampleList = new List<float>();
            byte[] readBuffer = new byte[4096];
            int bytesRead;

            while ((bytesRead = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                int sampleCount = bytesRead / 2;
                for (int i = 0; i < sampleCount; i++)
                {
                    short pcm = (short)(readBuffer[i * 2] | (readBuffer[i * 2 + 1] << 8));
                    sampleList.Add(pcm / 32768.0f);
                }
            }

            return sampleList.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load audio {filePath}: {ex.Message}");
            return Array.Empty<float>();
        }
    }
}

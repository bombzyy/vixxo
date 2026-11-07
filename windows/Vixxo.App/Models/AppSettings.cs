using System.Text.Json;
using Vixxo.Core.Audio;

namespace Vixxo.App.Models;

public sealed class AppSettings
{
    public string? SelectedPlaybackDeviceId { get; set; }
    public string? SelectedMonitorDeviceId { get; set; }
    public bool IsMonitoringEnabled { get; set; } = false;
    public int Port { get; set; } = 47800;
    public LatencyMode LatencyMode { get; set; } = LatencyMode.Balanced;
    public float GainDb { get; set; } = 0.0f;
    public bool IsMuted { get; set; } = false;
    public bool IsLimiterEnabled { get; set; } = true;
    public bool IsHighPassFilterEnabled { get; set; } = true;
    public bool IsNoiseGateEnabled { get; set; } = true;
    public float NoiseGateThresholdDb { get; set; } = -42.0f;
    public float SoundboardVolumeDb { get; set; } = -3.0f;
    public float MicVolumeDb { get; set; } = 0.0f;
    public bool SendClipsToVirtualMic { get; set; } = true;
    public bool MonitorClipsInHeadphones { get; set; } = false;
    public bool IsVoiceChangerEnabled { get; set; } = false;
    public VoicePreset ActiveVoicePreset { get; set; } = VoicePreset.Bypass;
    public float VoicePitchSemitones { get; set; } = 0.0f;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
}

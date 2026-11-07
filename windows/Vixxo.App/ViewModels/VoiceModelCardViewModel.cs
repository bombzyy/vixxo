using System.Windows.Input;
using Vixxo.Core.Audio;

namespace Vixxo.App.ViewModels;

public sealed class VoiceModelCardViewModel : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvatarEmoji { get; set; } = "🎙️";
    public string Tag { get; set; } = "AI RVC";
    public string TagColor { get; set; } = "#38BDF8";
    public VoicePreset Preset { get; set; } = VoicePreset.Bypass;
    public float DefaultPitch { get; set; } = 0.0f;
    public float DefaultFormant { get; set; } = 0.0f;
    public bool IsCustom { get; set; } = false;
    public string? FilePath { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ICommand? SelectCommand { get; set; }
}

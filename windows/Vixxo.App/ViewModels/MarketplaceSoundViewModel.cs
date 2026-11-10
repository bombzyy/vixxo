using System.Windows.Input;

namespace Vixxo.App.ViewModels;

public sealed class MarketplaceSoundViewModel : ObservableObject
{
    private string _id = "";
    private string _title = "";
    private string _category = "Valorant";
    private string _badge = "Tactical";
    private string _duration = "00:02";
    private string _desc = "";
    private string _audioPath = "";
    private string _icon = "bomb";
    private bool _isDownloading = false;
    private bool _isDownloaded = false;
    private bool _isPlaying = false;
    private string _statusText = "";

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public string Badge
    {
        get => _badge;
        set => SetProperty(ref _badge, value);
    }

    public string Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public string Desc
    {
        get => _desc;
        set => SetProperty(ref _desc, value);
    }

    public string AudioPath
    {
        get => _audioPath;
        set => SetProperty(ref _audioPath, value);
    }

    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(ActionBtnText));
            }
        }
    }

    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            if (SetProperty(ref _isDownloaded, value))
            {
                OnPropertyChanged(nameof(ActionBtnText));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayButtonIcon));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string ActionBtnText
    {
        get
        {
            if (_isDownloading) return "Downloading...";
            if (_isDownloaded) return "✓ Added to Pads";
            return "⬇ Add to Soundboard";
        }
    }

    public string PlayButtonIcon => _isPlaying ? "⏹" : "▶";

    public ICommand? PreviewCommand { get; set; }
    public ICommand? DownloadCommand { get; set; }
}

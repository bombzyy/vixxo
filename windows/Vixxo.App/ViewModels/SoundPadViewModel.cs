using System.Windows.Input;

namespace Vixxo.App.ViewModels;

public sealed class SoundPadViewModel : ObservableObject
{
    private string _id = "";
    private string _name = "";
    private string _shortcut = "";
    private string _duration = "00:00";
    private string _category = "Valorant";
    private bool _isEmpty = true;
    private bool _isPlaying = false;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Shortcut
    {
        get => _shortcut;
        set
        {
            if (SetProperty(ref _shortcut, value))
            {
                OnPropertyChanged(nameof(HasShortcut));
                OnPropertyChanged(nameof(ShortcutBadgeText));
            }
        }
    }

    public bool HasShortcut => !string.IsNullOrWhiteSpace(_shortcut);
    public string ShortcutBadgeText => string.IsNullOrWhiteSpace(_shortcut) ? "+ Bind" : _shortcut;

    public string Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            if (SetProperty(ref _isEmpty, value))
            {
                OnPropertyChanged(nameof(IsNotEmpty));
            }
        }
    }

    public bool IsNotEmpty => !_isEmpty;

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public ICommand? PlayCommand { get; set; }
    public ICommand? DeleteCommand { get; set; }
    public ICommand? ChangeShortcutCommand { get; set; }
}

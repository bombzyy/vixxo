using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using Vixxo.App.Models;

namespace Vixxo.App.Services;

public sealed class MarketplaceService
{
    private static readonly Lazy<MarketplaceService> _instance = new(() => new MarketplaceService());
    public static MarketplaceService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly MediaPlayer _previewPlayer;
    private string? _currentPlayingId;

    public event Action<string?, bool>? PlaybackStateChanged;

    public const string CatalogUrl = "https://vixxostudio.pages.dev/assets/data/sounds.json";
    public const string BaseMediaUrl = "https://vixxostudio.pages.dev/";

    public MarketplaceService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Vixxo-Desktop-Client/2.0");

        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Vixxo", "sounds", "marketplace");

        Directory.CreateDirectory(_cacheDirectory);

        _previewPlayer = new MediaPlayer();
        _previewPlayer.MediaEnded += (s, e) =>
        {
            _currentPlayingId = null;
            PlaybackStateChanged?.Invoke(null, false);
        };
        _previewPlayer.MediaFailed += (s, e) =>
        {
            _currentPlayingId = null;
            PlaybackStateChanged?.Invoke(null, false);
        };
    }

    public async Task<List<MarketplaceSound>> LoadCatalogAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(CatalogUrl);
            var items = JsonSerializer.Deserialize<List<MarketplaceSound>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items != null && items.Count > 0)
            {
                return items;
            }
        }
        catch
        {
            // Fallback to offline embedded catalog
        }

        return GetOfflineDefaultCatalog();
    }

    public async Task<string> DownloadSoundAsync(MarketplaceSound sound, IProgress<int>? progress = null)
    {
        string fileName = Path.GetFileName(sound.AudioPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{sound.Id}.mp3";
        }

        string targetPath = Path.Combine(_cacheDirectory, fileName);

        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            return targetPath;
        }

        string fullUrl = sound.AudioPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? sound.AudioPath
            : $"{BaseMediaUrl.TrimEnd('/')}/{sound.AudioPath.TrimStart('/')}";

        using var response = await _httpClient.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        await stream.CopyToAsync(fileStream);

        return targetPath;
    }

    public void PlayPreview(string soundId, string audioPath)
    {
        if (_currentPlayingId == soundId)
        {
            StopPreview();
            return;
        }

        StopPreview();

        try
        {
            string fileName = Path.GetFileName(audioPath);
            string localCached = Path.Combine(_cacheDirectory, fileName);

            Uri uriToPlay;
            if (File.Exists(localCached))
            {
                uriToPlay = new Uri(localCached, UriKind.Absolute);
            }
            else
            {
                string fullUrl = audioPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? audioPath
                    : $"{BaseMediaUrl.TrimEnd('/')}/{audioPath.TrimStart('/')}";
                uriToPlay = new Uri(fullUrl, UriKind.Absolute);
            }

            _previewPlayer.Open(uriToPlay);
            _previewPlayer.Volume = 0.85;
            _previewPlayer.Play();

            _currentPlayingId = soundId;
            PlaybackStateChanged?.Invoke(soundId, true);
        }
        catch
        {
            StopPreview();
        }
    }

    public void StopPreview()
    {
        try
        {
            _previewPlayer.Stop();
            _previewPlayer.Close();
        }
        catch { }

        _currentPlayingId = null;
        PlaybackStateChanged?.Invoke(null, false);
    }

    public bool IsSoundDownloaded(MarketplaceSound sound)
    {
        string fileName = Path.GetFileName(sound.AudioPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"{sound.Id}.mp3";
        string targetPath = Path.Combine(_cacheDirectory, fileName);
        return File.Exists(targetPath) && new FileInfo(targetPath).Length > 0;
    }

    public static List<MarketplaceSound> GetOfflineDefaultCatalog()
    {
        return new List<MarketplaceSound>
        {
            new() { Id = "val-spike-plant", Title = "Spike Planted", Category = "Valorant", Badge = "Tactical", Duration = "2.2s", Desc = "Real Valorant spike input beeps & lockdown thump", AudioPath = "assets/sounds/valorant/spike-plant.mp3", Icon = "bomb" },
            new() { Id = "val-spike-defuse", Title = "Spike Defused", Category = "Valorant", Badge = "Clutch", Duration = "1.8s", Desc = "Real Valorant defusal kit attach & round win chime", AudioPath = "assets/sounds/valorant/spike-defuse.mp3", Icon = "shield-check" },
            new() { Id = "val-ace", Title = "ACE Fanfare", Category = "Valorant", Badge = "Iconic", Duration = "3.5s", Desc = "Legendary full-team wipe electric guitar ACE riff", AudioPath = "assets/sounds/valorant/ace.mp3", Icon = "trophy" },
            new() { Id = "val-headshot", Title = "Headshot Dink", Category = "Valorant", Badge = "Crunch", Duration = "0.6s", Desc = "Crispy metallic Vandal/Phantom shield headshot", AudioPath = "assets/sounds/valorant/headshot.mp3", Icon = "crosshair" },
            new() { Id = "val-jett", Title = "Revive Me Jett", Category = "Valorant", Badge = "Meme", Duration = "2.0s", Desc = "Viral 'Revive me Jett, you can now use your ult!' clip", AudioPath = "assets/sounds/valorant/jett-revive.mp3", Icon = "wind" },

            new() { Id = "meme-airhorn", Title = "MLG Airhorn", Category = "Memes", Badge = "Classic", Duration = "1.4s", Desc = "High-decibel stadium airhorn triple-blast", AudioPath = "assets/sounds/memes/airhorn.mp3", Icon = "volume-2" },
            new() { Id = "meme-bruh", Title = "BRUH Sound", Category = "Memes", Badge = "Viral", Duration = "1.2s", Desc = "The original deep-vocal viral Bruh reaction", AudioPath = "assets/sounds/memes/bruh.mp3", Icon = "meh" },
            new() { Id = "meme-emotional-damage", Title = "Emotional Damage", Category = "Memes", Badge = "Trending", Duration = "1.5s", Desc = "Steven He's iconic 'Emotional Damage!' scream", AudioPath = "assets/sounds/memes/emotional-damage.mp3", Icon = "heart-crack" },
            new() { Id = "meme-sheesh", Title = "SHEEEESH", Category = "Memes", Badge = "Hype", Duration = "2.2s", Desc = "Ultra high-pitched flexing hype whistle", AudioPath = "assets/sounds/memes/sheesh.mp3", Icon = "flame" },
            new() { Id = "meme-sad-violin", Title = "Sad Violin", Category = "Memes", Badge = "Tragic", Duration = "3.8s", Desc = "World's smallest sad violin mournful melody", AudioPath = "assets/sounds/memes/sad-violin.mp3", Icon = "frown" },
            new() { Id = "meme-huh", Title = "Huh? Cat", Category = "Memes", Badge = "Reaction", Duration = "1.0s", Desc = "Confused viral cat echo 'Huh?!'", AudioPath = "assets/sounds/memes/huh.mp3", Icon = "help-circle" },
            new() { Id = "meme-record-scratch", Title = "Record Scratch", Category = "Memes", Badge = "Classic", Duration = "0.9s", Desc = "DJ vinyl needle scratch: 'Yup, that is me'", AudioPath = "assets/sounds/memes/record-scratch.mp3", Icon = "disc" },

            new() { Id = "game-bomb-planted", Title = "Bomb Planted (CS)", Category = "Gaming", Badge = "Tactical", Duration = "1.6s", Desc = "Counter-Strike radio: 'The bomb has been planted'", AudioPath = "assets/sounds/gaming/bomb-planted.mp3", Icon = "alert-triangle" },
            new() { Id = "game-gta-wasted", Title = "GTA V Wasted", Category = "Gaming", Badge = "Retro", Duration = "2.8s", Desc = "Sub-bass rumble & distorted brass death screen", AudioPath = "assets/sounds/gaming/gta-wasted.mp3", Icon = "skull" },
            new() { Id = "game-minecraft-oof", Title = "Minecraft Hurt Oof", Category = "Gaming", Badge = "Nostalgia", Duration = "0.5s", Desc = "Classic Steve legacy damage groan", AudioPath = "assets/sounds/gaming/minecraft-oof.mp3", Icon = "box" },

            new() { Id = "anime-nani", Title = "Omae Wa Mou... NANI!", Category = "Anime", Badge = "Legendary", Duration = "2.5s", Desc = "Fist of the North Star high-pitched laser ear-screech", AudioPath = "assets/sounds/anime/nani.mp3", Icon = "zap" },
            new() { Id = "anime-wow", Title = "Anime Wow", Category = "Anime", Badge = "Cute", Duration = "1.1s", Desc = "Fairy Tail / classic cute anime girl 'Woooow!'", AudioPath = "assets/sounds/anime/anime-wow.mp3", Icon = "sparkles" },

            new() { Id = "stream-cha-ching", Title = "Cash Cha-Ching", Category = "Streamer SFX", Badge = "Donation", Duration = "1.3s", Desc = "Heavy metal register drawer opening & coin ring", AudioPath = "assets/sounds/stream/cha-ching.mp3", Icon = "dollar-sign" },
            new() { Id = "stream-level-up", Title = "Level Up Chime", Category = "Streamer SFX", Badge = "Reward", Duration = "1.6s", Desc = "Sparkling 16-bit RPG quest completion fanfare", AudioPath = "assets/sounds/stream/level-up.mp3", Icon = "award" },
            new() { Id = "stream-wrong-buzzer", Title = "Wrong Answer Buzzer", Category = "Streamer SFX", Badge = "Fail", Duration = "1.2s", Desc = "Loud abrasive game-show failure double-buzz", AudioPath = "assets/sounds/stream/wrong-buzzer.mp3", Icon = "x-circle" }
        };
    }
}

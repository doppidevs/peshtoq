using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Peshtoq.Services;

/// <summary>
/// Reads and controls whatever is playing, through Windows' Global System Media Transport Controls.
/// Works with any app that reports to Windows media controls: Spotify, browsers (YouTube, Yandex Music web), etc.
/// </summary>
public sealed class MediaService
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshVersion;

    public event Action? Changed;

    public bool HasSession => _session is not null;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Source { get; private set; } = "";
    public BitmapImage? Cover { get; private set; }
    public bool IsPlaying { get; private set; }
    public int SessionCount { get; private set; }

    private TimeSpan _position, _duration;
    private DateTimeOffset _positionUpdated;

    public MediaService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public async Task InitAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            return; // media controls unavailable on this system
        }
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.InvokeAsync(() => Attach(_manager.GetCurrentSession()));
        _manager.SessionsChanged += (_, _) => _dispatcher.InvokeAsync(OnSessionsChanged);
        Attach(_manager.GetCurrentSession());
    }

    private void OnSessionsChanged()
    {
        if (_manager is null) return;
        var sessions = _manager.GetSessions();
        SessionCount = sessions.Count;
        if (_session is null || !sessions.Any(s => s.SourceAppUserModelId == _session.SourceAppUserModelId))
            Attach(_manager.GetCurrentSession());
        else
            Changed?.Invoke();
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged -= OnSessionEvent;
            _session.TimelinePropertiesChanged -= OnSessionEvent;
        }
        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionEvent;
            _session.PlaybackInfoChanged += OnSessionEvent;
            _session.TimelinePropertiesChanged += OnSessionEvent;
        }
        SessionCount = _manager?.GetSessions().Count ?? 0;
        _ = RefreshAsync();
    }

    private void OnSessionEvent<T>(GlobalSystemMediaTransportControlsSession sender, T args) =>
        _dispatcher.InvokeAsync(() => RefreshAsync());

    private async Task RefreshAsync()
    {
        int version = ++_refreshVersion;
        var session = _session;
        if (session is null)
        {
            Title = Artist = Source = "";
            Cover = null;
            IsPlaying = false;
            _duration = TimeSpan.Zero;
            Changed?.Invoke();
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var timeline = session.GetTimelineProperties();
            _position = timeline.Position;
            _duration = timeline.EndTime - timeline.StartTime;
            _positionUpdated = timeline.LastUpdatedTime;

            Source = FriendlySource(session.SourceAppUserModelId);

            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _refreshVersion) return; // a newer refresh started meanwhile

            string newTitle = props?.Title ?? "";
            bool trackChanged = newTitle != Title || (props?.Artist ?? "") != Artist;
            Title = newTitle;
            Artist = props?.Artist ?? "";

            if (trackChanged || Cover is null)
            {
                Cover = props?.Thumbnail is null ? null : await LoadThumbnailAsync(props.Thumbnail);
                if (version != _refreshVersion) return;
            }
        }
        catch
        {
            // sessions disappear mid-call when an app closes; the next event will fix the state
        }
        Changed?.Invoke();
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 240;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Current position (interpolated while playing) and track length.</summary>
    public (TimeSpan Position, TimeSpan Duration) Timeline()
    {
        if (_duration <= TimeSpan.Zero) return (TimeSpan.Zero, TimeSpan.Zero);
        var pos = _position;
        if (IsPlaying && _positionUpdated != default)
            pos += DateTimeOffset.Now - _positionUpdated;
        if (pos > _duration) pos = _duration;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        return (pos, _duration);
    }

    public async void TogglePlayPause() { try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session is not null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session is not null) await _session.TrySkipPreviousAsync(); } catch { } }

    public async void Seek(double fraction)
    {
        if (_session is null || _duration <= TimeSpan.Zero) return;
        try
        {
            var target = TimeSpan.FromTicks((long)(_duration.Ticks * Math.Clamp(fraction, 0, 1)));
            await _session.TryChangePlaybackPositionAsync(target.Ticks);
        }
        catch { }
    }

    /// <summary>Switch to the next app that has a media session (e.g. Spotify → Chrome).</summary>
    public void CycleSource()
    {
        if (_manager is null) return;
        var sessions = _manager.GetSessions().ToList();
        if (sessions.Count < 2) return;
        int i = _session is null ? -1 : sessions.FindIndex(s => s.SourceAppUserModelId == _session.SourceAppUserModelId);
        Attach(sessions[(i + 1) % sessions.Count]);
    }

    private static string FriendlySource(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        string id = aumid;
        if (id.Contains('!')) id = id[(id.LastIndexOf('!') + 1)..];       // packaged app: Publisher.App_hash!App
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        string lower = id.ToLowerInvariant();
        return lower switch
        {
            var s when s.Contains("chrome") => "Chrome",
            var s when s.Contains("msedge") || s.Contains("edge") => "Edge",
            var s when s.Contains("firefox") || s.StartsWith("308046b0af4a39cb") => "Firefox",
            var s when s.Contains("spotify") => "Spotify",
            var s when s.Contains("yandex") => "Yandex",
            var s when s.Contains("zunemusic") || s.Contains("mediaplayer") => "Media Player",
            var s when s.Contains("telegram") => "Telegram",
            _ => char.ToUpper(id[0]) + id[1..],
        };
    }
}

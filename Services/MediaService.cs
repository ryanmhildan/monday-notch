using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;

namespace MondayNotch.Services;

/// <summary>
/// Follows whatever app Windows considers the current media session (Spotify, a browser tab, etc.)
/// through the system media transport controls. <see cref="Changed"/> fires on a background thread.
/// </summary>
public sealed class MediaService : IDisposable
{
    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _propertiesVersion;

    private TimeSpan _position;
    private TimeSpan _startTime;
    private DateTimeOffset _positionUpdated;

    public event Action? Changed;

    public bool HasSession => _session is not null;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public byte[]? Thumbnail { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool CanTogglePlay { get; private set; }
    public bool CanPrevious { get; private set; }
    public bool CanNext { get; private set; }
    public bool CanSeek { get; private set; }
    public TimeSpan Duration { get; private set; }

    /// <summary>Players only report position occasionally, so it is extrapolated while playing.</summary>
    public TimeSpan Position
    {
        get
        {
            TimeSpan position = _position;
            if (IsPlaying && _positionUpdated != default)
                position += DateTimeOffset.Now - _positionUpdated;
            if (position < TimeSpan.Zero)
                position = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && position > Duration)
                position = Duration;
            return position;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception)
        {
            // Media session API unavailable (e.g. stripped-down Windows editions): music card stays empty.
            return;
        }

        _manager.CurrentSessionChanged += (_, _) => PickSession();
        _manager.SessionsChanged += (_, _) => PickSession();
        PickSession();
    }

    public void TogglePlayPause() => Run(s => s.TryTogglePlayPauseAsync());
    public void Next() => Run(s => s.TrySkipNextAsync());
    public void Previous() => Run(s => s.TrySkipPreviousAsync());

    public void Seek(TimeSpan position)
    {
        _position = position;
        _positionUpdated = DateTimeOffset.Now;
        Run(s => s.TryChangePlaybackPositionAsync((_startTime + position).Ticks));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Detach(_session);
            _session = null;
        }
        _manager = null;
    }

    private void PickSession()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager = _manager;
        if (manager is null)
            return;

        GlobalSystemMediaTransportControlsSession? next;
        try
        {
            next = manager.GetCurrentSession() ?? manager.GetSessions().FirstOrDefault();
        }
        catch (Exception)
        {
            next = null;
        }

        lock (_gate)
        {
            if (SameSession(_session, next))
                return;
            Detach(_session);
            _session = next;
            if (next is not null)
            {
                next.MediaPropertiesChanged += OnMediaPropertiesChanged;
                next.PlaybackInfoChanged += OnPlaybackInfoChanged;
                next.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        if (next is null)
        {
            Title = Artist = Album = "";
            Thumbnail = null;
            IsPlaying = CanTogglePlay = CanPrevious = CanNext = CanSeek = false;
            Duration = _position = TimeSpan.Zero;
            Changed?.Invoke();
            return;
        }

        ReadPlayback(next);
        ReadTimeline(next);
        Changed?.Invoke();
        _ = ReadPropertiesAsync(next);
    }

    private static bool SameSession(GlobalSystemMediaTransportControlsSession? a, GlobalSystemMediaTransportControlsSession? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        try
        {
            return a.SourceAppUserModelId == b.SourceAppUserModelId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Detach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null)
            return;
        try
        {
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch (Exception)
        {
            // The owning app already closed the session.
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = ReadPropertiesAsync(sender);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        ReadPlayback(sender);
        Changed?.Invoke();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        ReadTimeline(sender);
        Changed?.Invoke();
    }

    private void ReadPlayback(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
            bool wasPlaying = IsPlaying;
            IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            CanTogglePlay = info.Controls.IsPlayPauseToggleEnabled || info.Controls.IsPlayEnabled || info.Controls.IsPauseEnabled;
            CanPrevious = info.Controls.IsPreviousEnabled;
            CanNext = info.Controls.IsNextEnabled;
            CanSeek = info.Controls.IsPlaybackPositionEnabled;

            // Freeze or restart the extrapolated position at the moment playback changes.
            if (wasPlaying != IsPlaying)
            {
                _position = Position;
                _positionUpdated = DateTimeOffset.Now;
            }
        }
        catch (Exception)
        {
        }
    }

    private void ReadTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = session.GetTimelineProperties();
            _startTime = timeline.StartTime;
            Duration = timeline.EndTime - timeline.StartTime;
            _position = timeline.Position - timeline.StartTime;
            _positionUpdated = timeline.LastUpdatedTime;
        }
        catch (Exception)
        {
        }
    }

    private async Task ReadPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        int version = Interlocked.Increment(ref _propertiesVersion);
        try
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties properties = await session.TryGetMediaPropertiesAsync();

            byte[]? thumbnail = null;
            if (properties.Thumbnail is not null)
            {
                using var source = await properties.Thumbnail.OpenReadAsync();
                using Stream stream = source.AsStreamForRead();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                thumbnail = buffer.ToArray();
            }

            // A newer track change may have started while this one was loading.
            if (version != _propertiesVersion || !SameSession(session, _session))
                return;

            Title = properties.Title ?? "";
            Artist = string.IsNullOrEmpty(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist;
            Album = properties.AlbumTitle ?? "";
            Thumbnail = thumbnail;
            Changed?.Invoke();
        }
        catch (Exception)
        {
        }
    }

    private async void Run(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        GlobalSystemMediaTransportControlsSession? session = _session;
        if (session is null)
            return;
        try
        {
            await command(session);
        }
        catch (Exception)
        {
        }
    }
}

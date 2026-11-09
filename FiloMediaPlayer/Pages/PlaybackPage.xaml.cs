
using CommunityToolkit.Maui.Core;
using FiloMediaPlayer.DTOs;
using FiloMediaPlayer.Models.Bookmarks;
using FiloMediaPlayer.Services;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FiloMediaPlayer.Components;

namespace FiloMediaPlayer.Pages;

public partial class PlaybackPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }
    public int PlaylistId { get; private set; }
    private readonly FiloApiService _apiService;
    private readonly Random _random = new Random();
    private bool _isLoopEnabled = false;
    private bool _isPlaying = true;
    private bool _isSeeking = false;
    private PlaybackMediaResponse? _currentPlaybackMedia;
    private PlaybackMode _currentPlaybackMode = PlaybackMode.Normal;
    private BookmarkEditorPage? _bookmarkEditor;

    List<int> _playlistOrder = new();
    List<int> _playbackTimeline = new();
    int _currentIndex;
    private int _liveIndex;
    private bool _playbackInitialized = false;
    private readonly BookmarkTimelineDrawable _bookmarkTimelineDrawable = new();
    private List<BookmarkResponse> _timelineBookmarks = new();

    public enum PlaybackMode
    {
        Normal,
        ShuffleAll,
        RandomNoRepeat,
        TrueRandom
    }

    public enum PlaylistSortMode
    {
        PlaylistOrder,
        AlphabeticalAscending,
        AlphabeticalDescending,
        Reverse
    }

    public PlaybackPage(FiloApiService apiService)
    {
        InitializeComponent();
        BookmarkTimelineGraphics.Drawable = _bookmarkTimelineDrawable;
        _apiService = apiService;

        BookmarkPanel.SeekRequested += async (sender, seconds) =>
        {
            await MediaPlayer.SeekTo(
                TimeSpan.FromSeconds(seconds));
        };

        BookmarkPanel.AddRequested += (sender, e) => OpenBookmarkEditor();

        BookmarkPanel.OptionsRequested += async (sender, bookmark) =>
        {
            await ShowBookmarkOptionsAsync(bookmark);
        };
    }


    private async Task ShowBookmarkOptionsAsync(BookmarkResponse bookmark)
    {
        string action = await DisplayActionSheetAsync(
            bookmark.Title,
            "Cancel",
            null,
            "Edit",
            "Delete");

        switch (action)
        {
            case "Edit":
                OpenBookmarkEditor(bookmark);
                break;

            case "Delete":
                await DeleteBookmarkAsync(bookmark);
                break;
        }
    }

    private void OpenBookmarkEditor(BookmarkResponse? bookmark = null)
    {
        if (MediaFileId <= 0 || _currentPlaybackMedia == null)
            return;

        // Prevent editing a bookmark from a different media file.
        if (bookmark != null && bookmark.MediaFileId != MediaFileId)
            return;

        BookmarkPanel.Collapse();

        // Close an existing editor before opening another.
        if (_bookmarkEditor != null)
            CloseBookmarkEditor();

        double position = MediaPlayer.Position.TotalSeconds;
        double duration = MediaPlayer.Duration.TotalSeconds;

        if (!double.IsFinite(position) || position < 0)
            position = 0;

        if (!double.IsFinite(duration) || duration < 0)
            duration = 0;

        var editor = new BookmarkEditorPage(
            _apiService,
            MediaFileId,
            position,
            duration,
            bookmark);

        editor.PlaybackPositionProvider =
            () => MediaPlayer.Position.TotalSeconds;

        editor.CancelRequested += OnBookmarkEditorCancelRequested;
        editor.BookmarkSaved += OnBookmarkEditorSaved;

        _bookmarkEditor = editor;

        BookmarkEditorHost.Content = editor;
        BookmarkEditorContainer.IsVisible = true;
    }


    private async Task DeleteBookmarkAsync(BookmarkResponse bookmark)
    {
        // Confirm before deleting anything.
        bool confirmed = await DisplayAlertAsync(
            "Delete Bookmark",
            $"Are you sure you want to delete '{bookmark.Title}'?",
            "Delete",
            "Cancel");

        if (!confirmed)
            return;

        // Protect against deleting an item from another media file.
        if (bookmark.MediaFileId != MediaFileId)
            return;

        int mediaId = MediaFileId;

        try
        {
            using var response = await _apiService.DeleteAsync(
                $"api/media-bookmarks/{bookmark.MediaBookmarkId}");

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Bookmark deletion failed: {error}");

                await ShowPlaybackAlertAsync(
                    "Delete Failed",
                    "Unable to delete the bookmark.");

                return;
            }

            // Refresh only if we're still viewing the same media.
            if (mediaId == MediaFileId)
            {
                await LoadBookmarks();
            }
        }
        catch (UnauthorizedAccessException)
        {
            await ShowPlaybackAlertAsync(
                "Session Expired",
                "Please log in again.");
        }
        catch (HttpRequestException)
        {
            await ShowPlaybackAlertAsync(
                "Connection Error",
                "Unable to contact the server.");
        }
        catch (TaskCanceledException)
        {
            await ShowPlaybackAlertAsync(
                "Connection Timeout",
                "The deletion request timed out.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bookmark deletion error: {ex}");

            await ShowPlaybackAlertAsync(
                "Delete Error",
                "An unexpected error occurred while deleting the bookmark.");
        }
    }


    private void CloseBookmarkEditor()
    {
        if (_bookmarkEditor == null)
            return;

        _bookmarkEditor.CancelRequested -= OnBookmarkEditorCancelRequested;
        _bookmarkEditor.BookmarkSaved -= OnBookmarkEditorSaved;
        _bookmarkEditor.PlaybackPositionProvider = null;

        BookmarkEditorContainer.IsVisible = false;
        BookmarkEditorHost.Content = null;
        _bookmarkEditor = null;
    }

    private void OnBookmarkEditorCancelRequested(object? sender, EventArgs e)
    {
        CloseBookmarkEditor();
    }

    private async void OnBookmarkEditorSaved(object? sender, EventArgs e)
    {
        // Ignore completion from an editor belonging to old media.
        if (!ReferenceEquals(sender, _bookmarkEditor))
            return;

        CloseBookmarkEditor();
        await LoadBookmarks();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Opening and closing modals can trigger OnAppearing again.
        // Do not reload playback or destroy the bookmark editor.
        if (_playbackInitialized)
            return;

        _playbackInitialized = true;

        try
        {
            bool initialized = await LoadPlaylistOrder();

            if (!initialized)
            {
                _playbackInitialized = false;
                return;
            }

            bool loaded = await LoadPlaybackMedia();

            if (!loaded)
            {
                _playbackInitialized = false;
            }
        }
        catch
        {
            _playbackInitialized = false;
            throw;
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("MediaFileId", out var mediaData))
        {
            MediaFileId = (int)mediaData;
        }

        if (query.TryGetValue("PlaylistId", out var playlistData))
        {
            PlaylistId = (int)playlistData;
        }

        if (query.TryGetValue("PlaylistOrder", out var order))
        {
            _playlistOrder = (List<int>)order;
        }
    }



    private async Task<bool> LoadPlaybackMedia()
    {
        // Never leave an editor tied to the previous song/video open.
        CloseBookmarkEditor();
        _currentPlaybackMedia = null;

        try
        {
            BookmarkPanel.SetBookmarks(new List<BookmarkResponse>());
            _bookmarkTimelineDrawable.Bookmarks = Array.Empty<BookmarkResponse>();

            _bookmarkTimelineDrawable.DurationSeconds = 0;

            BookmarkTimelineGraphics.Invalidate();
            using var response = await _apiService.GetWithRetryAsync(
                $"api/media/{MediaFileId}/playback");

            if (!response.IsSuccessStatusCode)
            {
                await ShowPlaybackAlertAsync(
                    "Playback Error",
                    "Unable to retrieve media information.");

                return false;
            }

            var playbackMedia = await response.Content
                .ReadFromJsonAsync<PlaybackMediaResponse>();

            if (playbackMedia == null ||
                string.IsNullOrWhiteSpace(playbackMedia.PlaybackUrl))
            {
                await ShowPlaybackAlertAsync(
                    "Playback Error",
                    "The server returned invalid playback information.");

                return false;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                BindingContext = playbackMedia;

                MediaThumbnail.IsVisible =
                    playbackMedia.ContentType?.StartsWith(
                        "audio/",
                        StringComparison.OrdinalIgnoreCase) == true;

                MediaPlayer.Source = playbackMedia.PlaybackUrl;
            });

            _currentPlaybackMedia = playbackMedia;
            await LoadBookmarks();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            await ShowPlaybackAlertAsync(
                "Session Expired",
                "Please log in again.");
        }
        catch (HttpRequestException)
        {
            await ShowPlaybackAlertAsync(
                "Connection Error",
                "Unable to contact the server.");
        }
        catch (TaskCanceledException)
        {
            await ShowPlaybackAlertAsync(
                "Connection Timeout",
                "The playback request timed out.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playback loading error: {ex}");

            await ShowPlaybackAlertAsync(
                "Playback Error",
                "Unable to load this media.");
        }
        return false;
    }



    private void OnPlayPauseClicked(object sender, EventArgs e)
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseButton.Text = "Play";
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseButton.Text = "Pause";
        }
    }

    private void OnVolumeChanged(object sender, ValueChangedEventArgs e)
    {
        MediaPlayer.Volume = e.NewValue;
    }

    private void OnMediaOpened(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlaybackSlider.Maximum = MediaPlayer.Duration.TotalSeconds;
            _bookmarkTimelineDrawable.DurationSeconds = MediaPlayer.Duration.TotalSeconds;
            BookmarkTimelineGraphics.Invalidate();
            TotalTimeLabel.Text = $"{(int)MediaPlayer.Duration.TotalMinutes}:{MediaPlayer.Duration.Seconds:D2}";
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseButton.Text = "Pause";
        });
    }

    private async void OnMediaEnded(object sender, EventArgs e)
    {
        if (_isLoopEnabled)
        {
            await LoadPlaybackMedia();
        }
        else
        {
            await MoveToNextSong();
        }

    }

    private async void OnNextClicked(object sender, EventArgs e)
    {
        if (_isLoopEnabled)
        {
            _isLoopEnabled = false;
            LoopButton.Text = "Loop";
        }

        await MoveToNextSong();
    }

    private async void OnPreviousClicked(object sender, EventArgs e)
    {
        if (_playlistOrder.Count == 0 || _playbackTimeline.Count == 0)
            return;

        var previousMediaId = MediaFileId;
        var previousIndex = _currentIndex;
        var previousLiveIndex = _liveIndex;
        var previousTimeline = new List<int>(_playbackTimeline);

        if (_currentIndex > 0)
        {
            _currentIndex--;
        }

        else
        {
            if (_currentPlaybackMode == PlaybackMode.Normal)
            {
                var index = _playlistOrder.IndexOf(MediaFileId);
                if (index == 0)
                {
                    var endMediaId = _playlistOrder.Last();
                    _playbackTimeline.Insert(0, endMediaId);
                }
                else
                {
                    var prevMedia = _playlistOrder[index - 1];
                    _playbackTimeline.Insert(0, prevMedia);
                }
                _liveIndex++;
            }

            else if (_currentPlaybackMode == PlaybackMode.RandomNoRepeat)
            {
                if (_currentIndex > 0)
                {
                    _currentIndex--;
                }
                else
                {
                    return;
                }
            }

            else if (_currentPlaybackMode == PlaybackMode.ShuffleAll)
            {
                if (_currentIndex > 0)
                {
                    _currentIndex--;
                }
                else
                {
                    return;
                }
            }

            else if (_currentPlaybackMode == PlaybackMode.TrueRandom)
            {
                if (_currentIndex > 0)
                {
                    _currentIndex--;
                }
                else
                {
                    return;
                }
            }
        }

        MediaFileId = _playbackTimeline[_currentIndex];

        bool loaded = await LoadPlaybackMedia();

        if (!loaded)
        {
            MediaFileId = previousMediaId;
            _currentIndex = previousIndex;
            _liveIndex = previousLiveIndex;
            _playbackTimeline = previousTimeline;
        }

        UpdatePreviousButtonState();
    }

    private async Task MoveToNextSong()
    {
        if (_playlistOrder.Count == 0 || _playbackTimeline.Count == 0)
            return;

        var previousMediaId = MediaFileId;
        var previousIndex = _currentIndex;
        var previousLiveIndex = _liveIndex;
        var previousTimeline = new List<int>(_playbackTimeline);

        if (_currentPlaybackMode == PlaybackMode.Normal)
        {
            _currentIndex++;
            if (_currentIndex > _liveIndex)
            {
                _liveIndex = _currentIndex;
            }
            if (_currentIndex >= _playbackTimeline.Count)
            {
                for (int i = 0; i < _playlistOrder.Count; i++)
                {
                    _playbackTimeline.Add(_playlistOrder[i]);
                }
            }
        }

        else if (_currentPlaybackMode == PlaybackMode.RandomNoRepeat)
        {
            if (_currentIndex >= _liveIndex)
            {
                BuildRandomNoRepeatFuture();
            }

            _currentIndex++;

            if (_currentIndex > _liveIndex)
            {
                _liveIndex = _currentIndex;
            }
        }

        else if (_currentPlaybackMode == PlaybackMode.ShuffleAll)
        {
            if (_currentIndex >= _playbackTimeline.Count - 1)
            {
                BuildShuffleAllFuture();
            }

            _currentIndex++;

            if (_currentIndex > _liveIndex)
            {
                _liveIndex = _currentIndex;
            }
        }

        else if (_currentPlaybackMode == PlaybackMode.TrueRandom)
        {
            if (_currentIndex >= _playbackTimeline.Count - 1)
            {
                BuildTrueRandomFuture();
            }

            _currentIndex++;

            if (_currentIndex > _liveIndex)
            {
                _liveIndex = _currentIndex;
            }
        }


        MediaFileId = _playbackTimeline[_currentIndex];

        bool loaded = await LoadPlaybackMedia();

        if (!loaded)
        {
            // Restore the state from before Next was pressed.
            MediaFileId = previousMediaId;
            _currentIndex = previousIndex;
            _liveIndex = previousLiveIndex;
            _playbackTimeline = previousTimeline;
        }

        UpdatePreviousButtonState();

    }

    private void OnPositionChanged(object sender, MediaPositionChangedEventArgs e)
    {
        if (!_isSeeking)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var current = MediaPlayer.Position.TotalSeconds;
                PlaybackSlider.Value = current;
                CurrentTimeLabel.Text = $"{(int)MediaPlayer.Position.TotalMinutes}:{MediaPlayer.Position.Seconds:D2}";
                _bookmarkEditor?.UpdateCurrentPosition(current);
            });
        }
    }

    private void OnSeekStarted(object sender, EventArgs e)
    {
        _isSeeking = true;
        BookmarkHoverPreview.IsVisible = false;
    }

    private async void OnSeekCompleted(object sender, EventArgs e)
    {
        var newPosition = TimeSpan.FromSeconds(PlaybackSlider.Value);
        await MediaPlayer.SeekTo(newPosition);
        _isSeeking = false;
        _bookmarkEditor?.UpdateCurrentPosition(MediaPlayer.Position.TotalSeconds);
    }


    private async Task<bool> LoadPlaylistOrder()
    {
        if (_playlistOrder.Count == 0)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync(
                    $"api/playlists/{PlaylistId}/media");

                if (!response.IsSuccessStatusCode)
                {
                    await ShowPlaybackAlertAsync(
                        "Error",
                        "Unable to retrieve the playlist order.");
                    return false;
                }

                var media = await response.Content
                    .ReadFromJsonAsync<List<MediaResponse>>();

                if (media == null)
                {
                    await ShowPlaybackAlertAsync(
                        "Error",
                        "The server returned invalid playlist data.");
                    return false;
                }

                _playlistOrder = media
                    .Select(m => m.MediaFileId)
                    .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                await ShowPlaybackAlertAsync(
                    "Session Expired",
                    "Please log in again.");
                return false;
            }
            catch (HttpRequestException)
            {
                await ShowPlaybackAlertAsync(
    "Connection Error",
    "Unable to contact the server.");
                return false;
            }
            catch (TaskCanceledException)
            {
                await ShowPlaybackAlertAsync(
                    "Connection Timeout",
                    "The playlist request timed out.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Playlist order error: {ex}");

                await ShowPlaybackAlertAsync(
                    "Error",
                    "Unable to initialize the playlist.");
                return false;
            }
        }

        int startIndex = _playlistOrder.IndexOf(MediaFileId);

        if (startIndex < 0)
        {
            await ShowPlaybackAlertAsync(
                "Playback Error",
                "The selected media was not found in the playlist.");

            return false;
        }

        _playbackTimeline.Clear();
        _currentIndex = 0;
        _liveIndex = 0;

        for (int i = startIndex; i < _playlistOrder.Count; i++)
        {
            _playbackTimeline.Add(_playlistOrder[i]);
        }

        UpdatePreviousButtonState();

        return true;
    }


    private void OnPlaybackModeChanged(object sender, EventArgs e)
    {
        if (_playbackTimeline.Count == 0)
        {
            return;
        }
        switch (PlaybackModePicker.SelectedItem?.ToString())
        {
            case "Normal":
                BuildNormalFuture();
                _currentPlaybackMode = PlaybackMode.Normal;
                break;

            case "Shuffle All":
                BuildShuffleAllFuture();
                _currentPlaybackMode = PlaybackMode.ShuffleAll;
                break;

            case "Random No Repeat":
                BuildRandomNoRepeatFuture();
                _currentPlaybackMode = PlaybackMode.RandomNoRepeat;
                break;

            case "True Random":
                BuildTrueRandomFuture();
                _currentPlaybackMode = PlaybackMode.TrueRandom;
                break;
        }
        UpdatePreviousButtonState();
        Debug.Print($"Playback mode: {_currentPlaybackMode}");
    }

    private void BuildShuffleAllFuture()
    {
        var startIndex = _liveIndex + 1;
        var numToRemove = _playbackTimeline.Count - startIndex;
        _playbackTimeline.RemoveRange(startIndex, numToRemove);

        var shuffled = new List<int>(_playlistOrder);

        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            var randomIndex = _random.Next(0, i + 1);

            var stored = shuffled[i];
            shuffled[i] = shuffled[randomIndex];
            shuffled[randomIndex] = stored;
        }

        if (shuffled.Count > 1 && shuffled[0] == _playbackTimeline[_liveIndex])
        {
            var swapIndex = _random.Next(1, shuffled.Count);

            var stored = shuffled[0];
            shuffled[0] = shuffled[swapIndex];
            shuffled[swapIndex] = stored;
        }

        for (int i = 0; i < shuffled.Count; i++)
        {
            _playbackTimeline.Add(shuffled[i]);
        }


    }

    private void BuildNormalFuture()
    {
        var startIndex = _liveIndex + 1;
        var numToRemove = _playbackTimeline.Count - startIndex;
        _playbackTimeline.RemoveRange(startIndex, numToRemove);

        var liveMediaId = _playbackTimeline[_liveIndex];
        var playlistIndex = _playlistOrder.IndexOf(liveMediaId);

        var remainingMedia = new List<int>(_playlistOrder);
        remainingMedia.RemoveRange(0, playlistIndex + 1);

        _playbackTimeline.AddRange(remainingMedia);
    }

    private void BuildRandomNoRepeatFuture()
    {
        var startIndex = _liveIndex + 1;
        var numToRemove = _playbackTimeline.Count - startIndex;
        _playbackTimeline.RemoveRange(startIndex, numToRemove);

        var currentMedia = _playbackTimeline[_liveIndex];


        if (_playlistOrder.Count > 1)
        {
            var possibleMedia = _playlistOrder.Where(mediaId => mediaId != currentMedia).ToList();
            var randomIndex = _random.Next(0, possibleMedia.Count);
            var randomMedia = possibleMedia[randomIndex];
            _playbackTimeline.Add(randomMedia);
            Debug.Print($"RandomNoRepeat generated: {randomMedia}");
            Debug.Print($"Timeline: {string.Join(", ", _playbackTimeline)}");
            Debug.Print($"CurrentIndex: {_currentIndex}");
            Debug.Print($"LiveIndex: {_liveIndex}");
        }
        else
        {
            _playbackTimeline.Add(currentMedia);
        }

    }

    private void BuildTrueRandomFuture()
    {
        var startIndex = _liveIndex + 1;
        var numToRemove = _playbackTimeline.Count - startIndex;
        _playbackTimeline.RemoveRange(startIndex, numToRemove);

        var randomIndex = _random.Next(0, _playlistOrder.Count());
        var randomMedia = _playlistOrder[randomIndex];
        _playbackTimeline.Add(randomMedia);

    }

    private void UpdatePreviousButtonState()
    {
        if (_currentPlaybackMode == PlaybackMode.Normal)
        {
            PreviousButton.IsEnabled = true;
        }
        else
        {
            PreviousButton.IsEnabled = _currentIndex > 0;
        }
    }

    private void OnLoopButtonClicked(object sender, EventArgs e)
    {
        _isLoopEnabled = !_isLoopEnabled;
        LoopButton.Text = _isLoopEnabled ? "Loop On" : "Loop";
    }


    private Task ShowPlaybackAlertAsync(string title, string message)
    {
        return MainThread.InvokeOnMainThreadAsync(
            () => DisplayAlertAsync(title, message, "OK"));
    }

    private async Task LoadBookmarks()
    {
        int mediaId = MediaFileId;

        try
        {
            using var response = await _apiService.GetWithRetryAsync(
                $"api/media-bookmarks/media/{mediaId}");

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine(
                    $"Failed to load bookmarks: {response.StatusCode}");
                return;
            }

            var bookmarks = await response.Content
                .ReadFromJsonAsync<List<BookmarkResponse>>();

            if (mediaId != MediaFileId)
                return;

            var loadedBookmarks = bookmarks ?? new List<BookmarkResponse>();
            _timelineBookmarks = loadedBookmarks;
            BookmarkPanel.SetBookmarks(loadedBookmarks);

            _bookmarkTimelineDrawable.Bookmarks = loadedBookmarks;
            BookmarkTimelineGraphics.Invalidate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bookmark loading error: {ex}");
        }
    }


    private void OnBookmarkTimelinePointerMoved(
        object sender,
        PointerEventArgs e)
    {
        if (_isSeeking || _timelineBookmarks.Count == 0)
        {
            BookmarkHoverPreview.IsVisible = false;
            return;
        }

        double duration = MediaPlayer.Duration.TotalSeconds;
        double width = BookmarkTimelineGraphics.Width;

        if (!double.IsFinite(duration) || duration <= 0 ||
            !double.IsFinite(width) || width <= 0)
        {
            BookmarkHoverPreview.IsVisible = false;
            return;
        }

        var pointer = e.GetPosition(BookmarkTimelineGraphics);

        if (pointer == null)
        {
            BookmarkHoverPreview.IsVisible = false;
            return;
        }

        double mouseX = pointer.Value.X;

        if (mouseX < 0 || mouseX > width)
        {
            BookmarkHoverPreview.IsVisible = false;
            return;
        }

        // Same zero-inset mapping as the drawable.
        double GetX(double seconds)
        {
            return Math.Clamp(seconds / duration, 0, 1) * width;
        }

        BookmarkResponse? hoveredBookmark = null;
        double closestDistance = double.MaxValue;

        const double pointTolerance = 8;

        // Check point bookmarks first.
        foreach (var bookmark in _timelineBookmarks)
        {
            if (bookmark.EndSeconds.HasValue ||
                !double.IsFinite(bookmark.StartSeconds))
                continue;

            double bookmarkX = GetX(bookmark.StartSeconds);
            double distance = Math.Abs(mouseX - bookmarkX);

            if (distance <= pointTolerance &&
                distance < closestDistance)
            {
                hoveredBookmark = bookmark;
                closestDistance = distance;
            }
        }

        // Otherwise, check section ranges.
        if (hoveredBookmark == null)
        {
            foreach (var bookmark in _timelineBookmarks)
            {
                if (!bookmark.EndSeconds.HasValue)
                    continue;

                double start = bookmark.StartSeconds;
                double end = bookmark.EndSeconds.Value;

                if (!double.IsFinite(start) ||
                    !double.IsFinite(end) ||
                    end <= start)
                    continue;

                if (mouseX >= GetX(start) &&
                    mouseX <= GetX(end))
                {
                    hoveredBookmark = bookmark;
                    break;
                }
            }
        }

        if (hoveredBookmark == null)
        {
            BookmarkHoverPreview.IsVisible = false;
            return;
        }

        string startTime =
            FormatBookmarkTime(hoveredBookmark.StartSeconds);

        string timeText = hoveredBookmark.EndSeconds.HasValue
            ? $"{startTime} – {FormatBookmarkTime(hoveredBookmark.EndSeconds.Value)}"
            : startTime;

        BookmarkHoverLabel.Text =
            $"{hoveredBookmark.Title} ({timeText})";

        BookmarkHoverPreview.IsVisible = true;
    }

    private void OnBookmarkTimelinePointerExited(
        object sender,
        PointerEventArgs e)
    {
        BookmarkHoverPreview.IsVisible = false;
    }

    private static string FormatBookmarkTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;

        return TimeSpan.FromSeconds(Math.Floor(seconds))
            .ToString(@"hh\:mm\:ss");
    }

}
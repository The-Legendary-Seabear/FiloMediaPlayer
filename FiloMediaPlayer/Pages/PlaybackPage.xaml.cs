
using CommunityToolkit.Maui.Core;
using FiloMediaPlayer.DTOs;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FiloMediaPlayer.Services;

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

    List<int> _playlistOrder = new();
    List<int> _playbackTimeline = new();
    int _currentIndex;
    private int _liveIndex;

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
        _apiService = apiService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        bool initialized = await LoadPlaylistOrder();

        if (!initialized)
            return;

        await LoadPlaybackMedia();
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


    private async Task LoadPlaybackMedia()
    {
        try
        {
            using var response = await _apiService.GetWithRetryAsync(
                $"api/media/{MediaFileId}/playback");

            if (!response.IsSuccessStatusCode)
            {
                await ShowPlaybackAlertAsync(
                    "Playback Error",
                    "Unable to retrieve media information.");
                return;
            }

            var playbackMedia = await response.Content
                .ReadFromJsonAsync<PlaybackMediaResponse>();

            if (playbackMedia == null ||
                string.IsNullOrWhiteSpace(playbackMedia.PlaybackUrl))
            {
                await ShowPlaybackAlertAsync(
                    "Playback Error",
                    "The server returned invalid playback information.");
                return;
            }

            // Update the player only after successfully retrieving the data.
            _currentPlaybackMedia = playbackMedia;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                BindingContext = playbackMedia;

                MediaThumbnail.IsVisible =
                    playbackMedia.ContentType?.StartsWith(
                        "audio/",
                        StringComparison.OrdinalIgnoreCase) == true;

                MediaPlayer.Source = playbackMedia.PlaybackUrl;
            });
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
    }


    private void OnPlayPauseClicked(object sender, EventArgs e)
    {
        if(_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseButton.Text = "Play";
        } else
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
        if(_currentIndex > 0)
        {
            _currentIndex--;
        } 
        
        else
        {
            if (_currentPlaybackMode == PlaybackMode.Normal)
            {
                    var index = _playlistOrder.IndexOf(MediaFileId);
                    if(index == 0)
                    {
                        var endMediaId = _playlistOrder.Last();
                        _playbackTimeline.Insert(0, endMediaId);
                    } else
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

        UpdatePreviousButtonState();
        MediaFileId = _playbackTimeline[_currentIndex];
        await LoadPlaybackMedia();
    }

    private async Task MoveToNextSong()
    {
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


        UpdatePreviousButtonState();
        MediaFileId = _playbackTimeline[_currentIndex];
        await LoadPlaybackMedia();
    }

    private void OnPositionChanged(object sender, MediaPositionChangedEventArgs e)
    {
        if(!_isSeeking)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var current = MediaPlayer.Position.TotalSeconds;
                PlaybackSlider.Value = current;
                CurrentTimeLabel.Text = $"{(int)MediaPlayer.Position.TotalMinutes}:{MediaPlayer.Position.Seconds:D2}";
            });
        }
    }

    private void OnSeekStarted(object sender, EventArgs e)
    {
        _isSeeking = true;
    }

    private async void OnSeekCompleted(object sender, EventArgs e)
    {
        var newPosition = TimeSpan.FromSeconds(PlaybackSlider.Value);
        await MediaPlayer.SeekTo(newPosition);
        _isSeeking = false;
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

        for(int i = 0; i < shuffled.Count; i++)
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



}
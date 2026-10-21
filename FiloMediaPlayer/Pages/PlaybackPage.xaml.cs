
using CommunityToolkit.Maui.Core;
using FiloMediaPlayer.DTOs;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class PlaybackPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }
    private readonly HttpClient _httpClient;
    private bool _isPlaying = true;
    private bool _isSeeking = false;
    private PlaybackMediaResponse? _currentPlaybackMedia;
    public PlaybackPage()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadPlaybackMedia();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("PassedData", out var data))
        {
            MediaFileId = (int)data;
        }
    }

    private async Task LoadPlaybackMedia()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/media/{MediaFileId}/playback");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }
        _currentPlaybackMedia = await response.Content.ReadFromJsonAsync<PlaybackMediaResponse>() ?? new PlaybackMediaResponse();

        Debug.Print($"ContentType: {_currentPlaybackMedia.ContentType}");
        Debug.Print($"ThumbnailUrl: {_currentPlaybackMedia.ThumbnailUrl}");

        BindingContext = _currentPlaybackMedia;

        if(_currentPlaybackMedia.ContentType.StartsWith("audio/"))
        {
            MediaThumbnail.IsVisible = true;
        } else
        {
            MediaThumbnail.IsVisible = false;
        }

        MediaPlayer.Source = _currentPlaybackMedia.PlaybackUrl;
        MediaPlayer.Play();
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
        });
    }

    private void OnPositionChanged(object sender, MediaPositionChangedEventArgs e)
    {
        Debug.Print("Inside of On Position Changed");
        if(!_isSeeking)
        {
            Debug.Print("Inside of the !_isSeeking");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Debug.Print("Inside of the main thread");
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
}
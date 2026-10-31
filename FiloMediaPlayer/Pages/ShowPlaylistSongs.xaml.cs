using FiloMediaPlayer.DTOs;
using Microsoft.Maui.Controls;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;

namespace FiloMediaPlayer.Pages;

public partial class ShowPlaylistSongs : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly HttpClient _httpClient;

    private List<MediaResponse> _playlistMedia = new();
    private List<MediaResponse> _currentOrderedMedia = new();

    public enum PlaylistSortMode
    {
        PlaylistOrder,
        AlphabeticalAscending,
        AlphabeticalDescending,
        Reverse
    }

    private PlaylistSortMode _currentSortMode = PlaylistSortMode.PlaylistOrder;

    public ShowPlaylistSongs()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadSongs();
    }


    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("PassedData", out var data))
        {
            PlaylistId = (int)data;
        }
    }

    private async Task LoadSongs()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/playlists/{PlaylistId}/media");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }
        var media = await response.Content.ReadFromJsonAsync<List<MediaResponse>>();
        _playlistMedia = media ?? new List<MediaResponse>();

        SongUiCollection.ItemsSource = _playlistMedia;
    }

    private async void OnMediaTapped(object sender, TappedEventArgs e)
    {
        if (sender is Label tappedLabel && tappedLabel.BindingContext is MediaResponse media)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("MediaFileId", media.MediaFileId);
            parameters.Add("PlaylistId", PlaylistId);
            parameters.Add("PlaylistOrder", _currentOrderedMedia.Select(m => m.MediaFileId).ToList());

            await Shell.Current.GoToAsync("PlaybackPage", parameters);

        }
    }

    private void OnSortModeChanged(object sender, EventArgs e)
    {
        switch (SortModePicker.SelectedItem?.ToString())
        {
            case "Playlist Order":
                _currentSortMode = PlaylistSortMode.PlaylistOrder;
                break;

            case "A → Z":
                _currentSortMode = PlaylistSortMode.AlphabeticalAscending;
                break;

            case "Z → A":
                _currentSortMode = PlaylistSortMode.AlphabeticalDescending;
                break;

            case "Reverse":
                _currentSortMode = PlaylistSortMode.Reverse;
                break;
        }

        Debug.Print($"Sort mode: {_currentSortMode}");
        ApplySort();
    }

    private void ApplySort()
    {
        List<MediaResponse> sortedMedia;
        switch (_currentSortMode)
        {
            case PlaylistSortMode.AlphabeticalAscending:
                sortedMedia = _playlistMedia.OrderBy(m => m.Title).ToList();
                break;

            case PlaylistSortMode.AlphabeticalDescending:
                sortedMedia = _playlistMedia.OrderByDescending(m => m.Title).ToList();
                break;

            case PlaylistSortMode.Reverse:
                sortedMedia = _playlistMedia.AsEnumerable().Reverse().ToList();
                break;

            default:
                sortedMedia = new List<MediaResponse>(_playlistMedia);
                break;
        }
        SongUiCollection.ItemsSource = sortedMedia;
        _currentOrderedMedia = sortedMedia;
}

    private async void OnEditMediaClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not MediaResponse media)
        {
            return;
        }

        await Shell.Current.GoToAsync( "EditMediaPage", new Dictionary<string, object> { { "MediaFileId", media.MediaFileId } });
    }
}
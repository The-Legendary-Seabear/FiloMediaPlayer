using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ShowPlaylistsPage : ContentPage
{
    private readonly HttpClient _httpClient;
    private List<PlaylistResponse> _playlists = new();
    private List<MediaResponse> _media = new();
    private int addMediaToPlaylistId;
    public ShowPlaylistsPage()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadPlaylists();
    }

    private async Task LoadPlaylists()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync("api/playlists");
        if(!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }
        _playlists = await response.Content.ReadFromJsonAsync<List<PlaylistResponse>>() ?? new List<PlaylistResponse>();
        PlaylistUiCollection.ItemsSource = _playlists;
    }

    private async void OnPlaylistTapped(object sender, TappedEventArgs e)
    {
        if(sender is Label tappedLabel && tappedLabel.BindingContext is PlaylistResponse playlist)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("PassedData", playlist.PlaylistId);

            await Shell.Current.GoToAsync("ShowPlaylistSongs", parameters);
            
        }
    }

    private async void onAddMediaClicked(object sender, EventArgs e)
    {
        if(sender is Button addMedia && addMedia.BindingContext is PlaylistResponse playlist)
        {
            addMediaToPlaylistId = playlist.PlaylistId;
            AddMediaOverlay.IsVisible = true;
            await LoadAvailableMedia();
            AvailableMediaCollection.ItemsSource = _media;
        }
    }

    private async Task LoadAvailableMedia()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/playlists/{addMediaToPlaylistId}/available-media");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }
        _media = await response.Content.ReadFromJsonAsync<List<MediaResponse>>() ?? new List<MediaResponse>();
    }

    private async void onAddMediaToPlaylistClicked(object sender, EventArgs e)
    {
        if(sender is Button addMedia && addMedia.BindingContext is MediaResponse media)
        {
            var mediaId = media.MediaFileId;
            var response = await _httpClient.PostAsync($"api/playlists/{addMediaToPlaylistId}/media/{mediaId}", null);
            if(!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync("Media", "Could not add media", "close");
                return;
            } 
            _media.Remove(media);
            AvailableMediaCollection.ItemsSource = null;
            AvailableMediaCollection.ItemsSource = _media;
        }
    }

    private void OnSearchMediaTextChanged(object sender, TextChangedEventArgs e)
    {
        var typedText = SearchMedia.Text;
        if(string.IsNullOrEmpty(typedText))
        {
            AvailableMediaCollection.ItemsSource = _media;
            return;
        }

        var filteredList = _media.Where(p => p.Title.Contains(typedText, StringComparison.OrdinalIgnoreCase)).ToList();
        AvailableMediaCollection.ItemsSource = filteredList;
    }

    private void OnCloseAddMediaOverlay(object sender, EventArgs e)
    {
        AddMediaOverlay.IsVisible = false;
        SearchMedia.Text = string.Empty;
    }

    private async void onEditPlaylistInfo(object sender, EventArgs e)
    {
        if(sender is Button editButton && editButton.BindingContext is PlaylistResponse playlist)
        {
            var parameters = new Dictionary<string, object>
                {
                    { "PlaylistId", playlist.PlaylistId }
                };

            await Shell.Current.GoToAsync("EditPlaylistPage", parameters);
        }
    }
}
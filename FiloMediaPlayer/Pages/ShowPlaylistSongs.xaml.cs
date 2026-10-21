using FiloMediaPlayer.DTOs;
using Microsoft.Maui.Controls;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ShowPlaylistSongs : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly HttpClient _httpClient;

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
        SongUiCollection.ItemsSource = media;
    }

    private async void OnMediaTapped(object sender, TappedEventArgs e)
    {
        if (sender is Label tappedLabel && tappedLabel.BindingContext is MediaResponse media)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("PassedData", media.MediaFileId);

            await Shell.Current.GoToAsync("PlaybackPage", parameters);

        }
    }
}
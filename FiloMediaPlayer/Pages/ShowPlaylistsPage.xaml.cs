using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ShowPlaylistsPage : ContentPage
{
    private readonly HttpClient _httpClient;
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
        var playlists = await response.Content.ReadFromJsonAsync<List<PlaylistResponse>>();
        PlaylistUiCollection.ItemsSource = playlists;
    }
}
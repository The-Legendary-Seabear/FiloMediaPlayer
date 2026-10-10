

using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class CreatePlaylistPage : ContentPage
{
    private readonly HttpClient _httpClient;
    public CreatePlaylistPage()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
    }

	private async void OnCreatePlaylist(object sender, EventArgs e)
	{
		string playlistName = PlaylistNameEntry.Text;
		if(string.IsNullOrWhiteSpace(playlistName))
		{
			await DisplayAlertAsync("Playlist Name", "Enter a valid playlist name", "Close");
			return;
		}
		CreatePlaylistRequest req = new CreatePlaylistRequest(playlistName);
		var token = await SecureStorage.Default.GetAsync("accessToken");


        if (string.IsNullOrEmpty(token))
		{
			await DisplayAlertAsync("Token", "Could not access the token", "Close");
			return;
		}

		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue ("Bearer", token);
        var response = await _httpClient.PostAsJsonAsync("api/playlists/create", req);
		if(response.IsSuccessStatusCode)
		{
			await DisplayAlertAsync("Playlist", "Playlist Created", "Ok");
		} else
		{
            await DisplayAlertAsync("Playlist", "Something went wrong with creating the playlist", "Close");
        }

    }
}
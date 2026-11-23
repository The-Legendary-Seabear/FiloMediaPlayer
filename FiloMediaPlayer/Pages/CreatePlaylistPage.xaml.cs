
using FiloMediaPlayer.DTOs;

namespace FiloMediaPlayer.Pages;

public partial class CreatePlaylistPage : ContentPage
{
    private readonly FiloApiService _apiService;
    private bool _isCreating;

    public CreatePlaylistPage(FiloApiService apiService)
	{
		InitializeComponent();
        _apiService = apiService;
    }

	private async void OnCreatePlaylist(object sender, EventArgs e)
	{
        // Ignore repeated clicks while a request is in flight.
        if (_isCreating)
            return;

		string playlistName = PlaylistNameEntry.Text;
		if(string.IsNullOrWhiteSpace(playlistName))
		{
			await DisplayAlertAsync("Playlist Name", "Enter a valid playlist name", "Close");
			return;
		}

        _isCreating = true;

        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            CreatePlaylistRequest req = new CreatePlaylistRequest(playlistName);

            using var response = await _apiService.PostAsync("api/playlists/create", req);

            if (!response.IsSuccessStatusCode)
            {
                await ErrorPresenter.ShowAsync(this, response, "create the playlist");
                return;
            }

            await DisplayAlertAsync("Playlist", "Playlist Created", "Ok");
            PlaylistNameEntry.Text = string.Empty;
        }
        catch (Exception ex)
        {
            // Not retried automatically: the playlist may already exist.
            await ErrorPresenter.ShowAsync(
                this, ex, "create the playlist", mayHaveSucceeded: true);
        }
        finally
        {
            _isCreating = false;

            if (sender is Button finishedButton)
                finishedButton.IsEnabled = true;
        }
    }
}

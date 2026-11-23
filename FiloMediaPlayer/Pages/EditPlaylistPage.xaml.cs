using FiloMediaPlayer.DTOs;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class EditPlaylistPage : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly FiloApiService _apiService;

    /*
    private List<ThumbnailResponse> _thumbnails = new();
    private int? _selectedThumbnailId;
    */
    private bool _playlistLoaded;
    private bool _thumbnailsLoaded;
    private bool _isLoading;
    private bool _isSaving;

    public EditPlaylistPage(FiloApiService apiService)
	{
		InitializeComponent();
        _apiService = apiService;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("PlaylistId", out var data))
        {
            PlaylistId = (int)data;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // A failed load is retried the next time the page appears.
        // Each part loads once so a retry never overwrites the user's edits.
        if ((!_playlistLoaded || !_thumbnailsLoaded) && !_isLoading)
        {
            _isLoading = true;

            try
            {
                if (!_playlistLoaded)
                    _playlistLoaded = await LoadPlaylist();

                if (_playlistLoaded && !_thumbnailsLoaded)
                    _thumbnailsLoaded = await LoadThumbnails();
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    public async Task<bool> LoadPlaylist()
    {
        while (true)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync($"api/playlists/{PlaylistId}");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(this, response, "load the playlist"))
                        continue;

                    return false;
                }

                var playlist = await response.Content.ReadFromJsonAsync<PlaylistResponse>();

                if (playlist == null)
                {
                    await DisplayAlertAsync("Error", "The server returned invalid playlist data.", "Close");
                    return false;
                }

                PlaylistNameEntry.Text = playlist.Name;
                ThumbnailPicker.SelectedThumbnailId = playlist.ThumbnailId;
                return true;
            }
            catch (Exception ex)
            {
                if (await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load the playlist"))
                    continue;

                return false;
            }
        }
    }

    public async Task<bool> LoadThumbnails()
    {
        while (true)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync("api/thumbnails");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(this, response, "load your thumbnails"))
                        continue;

                    return false;
                }

                var thumbnails = await response.Content.ReadFromJsonAsync<List<ThumbnailResponse>>() ?? new List<ThumbnailResponse>();
                ThumbnailPicker.Thumbnails = thumbnails;
                return true;
            }
            catch (Exception ex)
            {
                if (await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load your thumbnails"))
                    continue;

                return false;
            }
        }
    }

    /*
    public async void OnThumbnailSelected(object sender, EventArgs e)
    {
        await DisplayAlertAsync("Debug", "Chose Thumbnail", "OK");

        if (sender is Button selected && selected.BindingContext is ThumbnailResponse thumbnail)
        {
            _selectedThumbnailId = thumbnail.ThumbnailId;
            SelectedThumbnailImage.Source = thumbnail.ImageUrl;
            SelectedThumbnailName.Text = thumbnail.Name;
        }
    }
    */

    public async void OnSaveChangesClicked(object sender, EventArgs e)
    {
        if (_isSaving)
            return;

        await DisplayAlertAsync("Debug","Save button clicked!","OK");

        // Saving before the playlist loaded would overwrite it with blanks.
        if (!_playlistLoaded)
        {
            await DisplayAlertAsync(
                "Not Loaded",
                "The playlist hasn't loaded yet, so it can't be saved. Go back and open it again.",
                "Close");
            return;
        }

        if(string.IsNullOrWhiteSpace(PlaylistNameEntry.Text))
        {
            await DisplayAlertAsync("Error", "Please insert a valid title", "Close");
            return;
        }

        var request = new UpdatePlaylistRequest
        {
            Name = PlaylistNameEntry.Text,
            ThumbnailId = ThumbnailPicker.SelectedThumbnailId
        };

        _isSaving = true;

        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            using var response = await _apiService.PutAsync($"api/playlists/{PlaylistId}", request);
            if(!response.IsSuccessStatusCode)
            {
                await ErrorPresenter.ShowAsync(this, response, "save the playlist");
                return;
            }

            await DisplayAlertAsync("Success", "The Playlist has successfully been changed", "Close");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            // The form keeps the user's changes so they can save again.
            await ErrorPresenter.ShowAsync(this, ex, "save the playlist", mayHaveSucceeded: true);
        }
        finally
        {
            _isSaving = false;

            if (sender is Button finishedButton)
                finishedButton.IsEnabled = true;
        }
    }

    /*
    private void OnRemoveThumbnailClicked(object sender, EventArgs e)
    {
        _selectedThumbnailId = null;
        SelectedThumbnailImage.Source = null;
        SelectedThumbnailName.Text = "No thumbnail selected";
    }
    */

    private async void OnManageMediaClicked(object sender, EventArgs e)
    {
        var parameters = new Dictionary<string, object>
            {
                { "PlaylistId", PlaylistId }
            };
        await Shell.Current.GoToAsync("ManagePlaylistMediaPage", parameters);
    }
}
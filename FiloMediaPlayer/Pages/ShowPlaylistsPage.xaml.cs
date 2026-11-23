using FiloMediaPlayer.DTOs;
using System.Net;
using System.Net.Http.Json;
using FiloMediaPlayer.Services;

namespace FiloMediaPlayer.Pages;

public partial class ShowPlaylistsPage : ContentPage
{
    private readonly FiloApiService _apiService;
    private List<PlaylistResponse> _playlists = new();
    private List<MediaResponse> _media = new();
    private int addMediaToPlaylistId;
    private bool _isLoadingPlaylists;
    private bool _isAddingMedia;

    public ShowPlaylistsPage(FiloApiService apiService)
    {
        InitializeComponent();

        _apiService = apiService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isLoadingPlaylists)
            return;

        _isLoadingPlaylists = true;

        try
        {
            await LoadPlaylists();
        }
        finally
        {
            _isLoadingPlaylists = false;
        }
    }


    private async Task LoadPlaylists()
    {
        // On failure the playlists already shown stay visible.
        while (true)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync(
                    "api/playlists");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(this, response, "load your playlists"))
                        continue;

                    return;
                }

                var playlists = await response.Content
                    .ReadFromJsonAsync<List<PlaylistResponse>>();

                if (playlists == null)
                {
                    throw new InvalidOperationException(
                        "The server returned an invalid playlist response.");
                }

                _playlists = playlists;
                PlaylistUiCollection.ItemsSource = _playlists;
                return;
            }
            catch (Exception ex)
            {
                if (!await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load your playlists"))
                    return;
            }
        }
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
        if (sender is Button addMedia &&
        addMedia.BindingContext is PlaylistResponse playlist)
        {
            addMediaToPlaylistId = playlist.PlaylistId;

            // Clear data from the previous overlay session.
            _media = new List<MediaResponse>();
            AvailableMediaCollection.ItemsSource = _media;

            AddMediaOverlay.IsVisible = true;

            await LoadAvailableMedia();
        }
    }


    private async Task LoadAvailableMedia()
    {
        while (true)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync(
                    $"api/playlists/{addMediaToPlaylistId}/available-media");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(this, response, "load the available media"))
                        continue;

                    return;
                }

                var media = await response.Content
                    .ReadFromJsonAsync<List<MediaResponse>>();

                if (media == null)
                {
                    throw new InvalidOperationException(
                        "The server returned invalid media data.");
                }

                _media = media;
                AvailableMediaCollection.ItemsSource = _media;
                return;
            }
            catch (Exception ex)
            {
                if (!await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load the available media"))
                    return;
            }
        }
    }



    private async void onAddMediaToPlaylistClicked(object sender, EventArgs e)
    {
        if (sender is not Button addButton ||
            addButton.BindingContext is not MediaResponse media)
            return;

        // One add at a time, so repeated clicks can't double-apply.
        if (_isAddingMedia)
            return;

        _isAddingMedia = true;
        addButton.IsEnabled = false;

        try
        {
            using var response = await _apiService.PostEmptyAsync(
                $"api/playlists/{addMediaToPlaylistId}/media/{media.MediaFileId}");

            // 409: an earlier attempt already added it.
            if (!response.IsSuccessStatusCode &&
                response.StatusCode != HttpStatusCode.Conflict)
            {
                await ErrorPresenter.ShowAsync(this, response, "add the media to the playlist");
                return;
            }

            _media.Remove(media);
            AvailableMediaCollection.ItemsSource = null;
            AvailableMediaCollection.ItemsSource = _media;
        }
        catch (Exception ex)
        {
            // Safe to try again: a repeat is reported as already added.
            await ErrorPresenter.ShowAsync(
                this, ex, "add the media to the playlist", mayHaveSucceeded: true);
        }
        finally
        {
            _isAddingMedia = false;
            addButton.IsEnabled = true;
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
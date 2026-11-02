using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FiloMediaPlayer.Services;

namespace FiloMediaPlayer.Pages;

public partial class ShowPlaylistsPage : ContentPage
{
    private readonly HttpClient _httpClient;
    private readonly FiloApiService _apiService;
    private List<PlaylistResponse> _playlists = new();
    private List<MediaResponse> _media = new();
    private int addMediaToPlaylistId;
    public ShowPlaylistsPage(FiloApiService apiService)
    {
        InitializeComponent();

        _apiService = apiService;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://localhost:7038/")
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadPlaylists();
    }


    private async Task LoadPlaylists()
    {
        try
        {
            using var response = await _apiService.GetWithRetryAsync(
                "api/playlists");

            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Connection Error",
                    "Unable to load playlists. Your existing data will remain visible.",
                    "OK");

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
        }
        catch (UnauthorizedAccessException)
        {
            await DisplayAlertAsync(
                "Session Expired",
                "Please log in again.",
                "OK");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Connection Error",
                "Unable to contact the server. Please try again.",
                "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync(
                "Request Timed Out",
                "The server took too long to respond. Please try again.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                "Error",
                $"Unable to load playlists: {ex.Message}",
                "OK");
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
        try
        {
            using var response = await _apiService.GetWithRetryAsync(
                $"api/playlists/{addMediaToPlaylistId}/available-media");

            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Error",
                    "Unable to load available media.",
                    "OK");
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
        }
        catch (UnauthorizedAccessException)
        {
            await DisplayAlertAsync(
                "Session Expired",
                "Please log in again.",
                "OK");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Connection Error",
                "Unable to contact the server. Please try again.",
                "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync(
                "Request Timed Out",
                "The server took too long to respond.",
                "OK");
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                "Error",
                "Unable to load available media.",
                "OK");
        }
    }



    private async void onAddMediaToPlaylistClicked(object sender, EventArgs e)
    {
        if (sender is not Button addButton ||
            addButton.BindingContext is not MediaResponse media)
            return;

        addButton.IsEnabled = false;

        try
        {
            var token = await SecureStorage.Default.GetAsync("accessToken");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/playlists/{addMediaToPlaylistId}/media/{media.MediaFileId}");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Error",
                    "Could not add media to the playlist.",
                    "OK");
                return;
            }

            _media.Remove(media);
            AvailableMediaCollection.ItemsSource = null;
            AvailableMediaCollection.ItemsSource = _media;
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Connection Error",
                "The connection was interrupted. The media may have been added. Reopen this window to refresh the list before trying again.",
                "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync(
                "Request Timed Out",
                "The request timed out. The media may have been added. Refresh the list before retrying.",
                "OK");
        }
        finally
        {
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

using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ManageMediaPlaylistsPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }

    private readonly HttpClient _httpClient;

    private List<PlaylistResponse> _currentPlaylists = new();
    private List<PlaylistResponse> _availablePlaylists = new();

    private bool _showingCurrentPlaylists = true;

    public string PlaylistActionText => _showingCurrentPlaylists ? "Remove" : "Add";

    public ManageMediaPlaylistsPage()
    {
        InitializeComponent();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://localhost:7038/")
        };

        BindingContext = this;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("MediaFileId", out var data))
        {
            MediaFileId = (int)data;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadCurrentPlaylists();
        await LoadAvailablePlaylists();

        UpdatePlaylistDisplay();
    }


    private async Task LoadCurrentPlaylists()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.GetAsync(
            $"api/media/{MediaFileId}/playlists");

        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Failed to load playlists.", "Close");
            return;
        }

        _currentPlaylists =
            await response.Content.ReadFromJsonAsync<List<PlaylistResponse>>()
            ?? new List<PlaylistResponse>();
    }

    private async Task LoadAvailablePlaylists()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.GetAsync(
            $"api/media/{MediaFileId}/available-playlists");

        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Failed to load available playlists.", "Close");
            return;
        }

        _availablePlaylists =
            await response.Content.ReadFromJsonAsync<List<PlaylistResponse>>()
            ?? new List<PlaylistResponse>();
    }

    private void OnCurrentPlaylistsClicked(object sender, EventArgs e)
    {
        _showingCurrentPlaylists = true;
        PlaylistSearchBar.Text = string.Empty;
        UpdatePlaylistDisplay();
    }

    private void OnAvailablePlaylistsClicked(object sender, EventArgs e)
    {
        _showingCurrentPlaylists = false;
        PlaylistSearchBar.Text = string.Empty;
        UpdatePlaylistDisplay();
    }

    private void OnPlaylistSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaylistDisplay();
    }

    private async void OnPlaylistActionClicked(object sender, EventArgs e)
    {
        if (sender is not Button button ||
        button.BindingContext is not PlaylistResponse playlist)
        {
            return;
        }

        button.IsEnabled = false;

        try
        {
            var token = await SecureStorage.Default.GetAsync("accessToken");

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;

            if (_showingCurrentPlaylists)
            {
                response = await _httpClient.DeleteAsync(
                    $"api/playlists/{playlist.PlaylistId}/media/{MediaFileId}");
            }
            else
            {
                response = await _httpClient.PostAsync(
                    $"api/playlists/{playlist.PlaylistId}/media/{MediaFileId}",
                    null);
            }

            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Error",
                    "Failed to update playlist membership.",
                    "Close");
                return;
            }

            await LoadCurrentPlaylists();
            await LoadAvailablePlaylists();

            UpdatePlaylistDisplay();
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Connection Error",
                "Unable to connect to the server.",
                "Close");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void UpdatePlaylistDisplay()
    {
        var playlists = _showingCurrentPlaylists
            ? _currentPlaylists
            : _availablePlaylists;

        var searchText = PlaylistSearchBar.Text ?? string.Empty;

        PlaylistCollection.ItemsSource = playlists
            .Where(p => p.Name.Contains(
                searchText,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        CurrentPlaylistsButton.BackgroundColor =
            _showingCurrentPlaylists ? Colors.DodgerBlue : Colors.Gray;

        AvailablePlaylistsButton.BackgroundColor =
            _showingCurrentPlaylists ? Colors.Gray : Colors.DodgerBlue;

        OnPropertyChanged(nameof(PlaylistActionText));
    }
}


using FiloMediaPlayer.DTOs;
using System.Net;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ManageMediaPlaylistsPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }

    private readonly FiloApiService _apiService;
    private bool _isBusy;

    private List<PlaylistResponse> _currentPlaylists = new();
    private List<PlaylistResponse> _availablePlaylists = new();

    private bool _showingCurrentPlaylists = true;

    public string PlaylistActionText => _showingCurrentPlaylists ? "Remove" : "Add";

    public ManageMediaPlaylistsPage(FiloApiService apiService)
    {
        InitializeComponent();

        _apiService = apiService;

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

        if (_isBusy)
            return;

        _isBusy = true;

        try
        {
            await LoadAllPlaylists();
        }
        finally
        {
            _isBusy = false;
        }
    }


    // On failure the lists already shown are kept.
    private async Task LoadAllPlaylists()
    {
        while (true)
        {
            try
            {
                var current = await LoadPlaylistList($"api/media/{MediaFileId}/playlists");
                var available = await LoadPlaylistList($"api/media/{MediaFileId}/available-playlists");

                _currentPlaylists = current;
                _availablePlaylists = available;
                UpdatePlaylistDisplay();
                return;
            }
            catch (Exception ex)
            {
                if (!await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load the playlists"))
                    return;
            }
        }
    }

    private async Task<List<PlaylistResponse>> LoadPlaylistList(string endpoint)
    {
        using var response = await _apiService.GetWithRetryAsync(endpoint);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrors.DescribeAsync(response, "load the playlists"),
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<List<PlaylistResponse>>()
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

        // One change at a time, so repeated clicks can't double-apply.
        if (_isBusy)
            return;

        _isBusy = true;
        button.IsEnabled = false;
        bool removing = _showingCurrentPlaylists;

        try
        {
            using var response = removing
                ? await _apiService.DeleteAsync(
                    $"api/playlists/{playlist.PlaylistId}/media/{MediaFileId}")
                : await _apiService.PostEmptyAsync(
                    $"api/playlists/{playlist.PlaylistId}/media/{MediaFileId}");

            // 404 on remove / 409 on add: an earlier attempt already
            // made this change, so just refresh the lists.
            bool alreadyDone = removing
                ? response.StatusCode == HttpStatusCode.NotFound
                : response.StatusCode == HttpStatusCode.Conflict;

            if (!response.IsSuccessStatusCode && !alreadyDone)
            {
                await ErrorPresenter.ShowAsync(this, response, "update playlist membership");
                return;
            }

            await LoadAllPlaylists();
        }
        catch (Exception ex)
        {
            // Safe to try again: a repeat is reported as already done.
            await ErrorPresenter.ShowAsync(
                this, ex, "update playlist membership", mayHaveSucceeded: true);
        }
        finally
        {
            button.IsEnabled = true;
            _isBusy = false;
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

        // Presentation only: see the matching note in ManagePlaylistMediaPage.
        // Style, not BackgroundColor - a direct colour assignment would
        // outrank the design-system style and reintroduce the mismatch.
        Style? selectedStyle = null;
        Style? unselectedStyle = null;

        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            if (resources.TryGetValue("Filo.Button.SegmentSelected", out var s))
            {
                selectedStyle = s as Style;
            }

            if (resources.TryGetValue("Filo.Button.SegmentUnselected", out var u))
            {
                unselectedStyle = u as Style;
            }
        }

        CurrentPlaylistsButton.Style =
            _showingCurrentPlaylists ? selectedStyle : unselectedStyle;

        AvailablePlaylistsButton.Style =
            _showingCurrentPlaylists ? unselectedStyle : selectedStyle;

        OnPropertyChanged(nameof(PlaylistActionText));
    }
}

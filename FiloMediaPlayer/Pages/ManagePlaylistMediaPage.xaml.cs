

using FiloMediaPlayer.DTOs;
using System.Net;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ManagePlaylistMediaPage : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly FiloApiService _apiService;
    private bool _isBusy;

    private List<MediaResponse> _currentMedia = new();
    private List<MediaResponse> _availableMedia = new();
    private bool _showingCurrentMedia = true;

    public string MediaActionText => _showingCurrentMedia ? "Remove" : "Add";
    public ManagePlaylistMediaPage(FiloApiService apiService)
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

        if (_isBusy)
            return;

        _isBusy = true;

        try
        {
            await LoadAllMedia();
        }
        finally
        {
            _isBusy = false;
        }
    }

    // On failure the lists already shown are kept.
    private async Task LoadAllMedia()
    {
        while (true)
        {
            try
            {
                var current = await LoadMediaList($"api/playlists/{PlaylistId}/media");
                var available = await LoadMediaList($"api/playlists/{PlaylistId}/available-media");

                _currentMedia = current;
                _availableMedia = available;
                UpdateMediaDisplay();
                return;
            }
            catch (Exception ex)
            {
                if (!await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load the playlist's media"))
                    return;
            }
        }
    }

    private async Task<List<MediaResponse>> LoadMediaList(string endpoint)
    {
        using var response = await _apiService.GetWithRetryAsync(endpoint);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrors.DescribeAsync(response, "load the playlist's media"),
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<List<MediaResponse>>() ?? new List<MediaResponse>();
    }

    private void UpdateMediaDisplay()
    {
        // Presentation only: pull the segment styles from the Filo design
        // system instead of hardcoding DodgerBlue/Gray. Assigning Style rather
        // than BackgroundColor is deliberate - a direct BackgroundColor set
        // outranks any style setter, which is what previously pinned these two
        // buttons to colours unrelated to the rest of the app.
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

        if(_showingCurrentMedia)
        {
            CurrentMediaButton.Style = selectedStyle;
            AvailableMediaButton.Style = unselectedStyle;
            MediaListTitle.Text = "Showing Current Media";
            MediaCollection.ItemsSource = _currentMedia;
        }
        else
        {
            CurrentMediaButton.Style = unselectedStyle;
            AvailableMediaButton.Style = selectedStyle;
            MediaListTitle.Text = "Showing Available Media";
            MediaCollection.ItemsSource = _availableMedia;
        }

        OnPropertyChanged(nameof(MediaActionText));
    }

    private void OnCurrentMediaClicked(object sender, EventArgs e)
    {
        _showingCurrentMedia = true;
        MediaSearchBar.Text = string.Empty;
        UpdateMediaDisplay();
    }

    private void OnAvailableMediaClicked(object sender, EventArgs e)
    {
        _showingCurrentMedia = false;
        MediaSearchBar.Text = string.Empty;
        UpdateMediaDisplay();

    }

    private void OnMediaSearchTextChanged(object sender,TextChangedEventArgs e)
    {
        var searchText = e.NewTextValue;
        List<MediaResponse> media;
        if (_showingCurrentMedia)
        {
            media = _currentMedia.Where(media => media.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        } 
        else
        {
            media = _availableMedia.Where(media => media.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        MediaCollection.ItemsSource = media;
    }

    private async void OnMediaActionClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not MediaResponse media)
            return;

        // One change at a time, so repeated clicks can't double-apply.
        if (_isBusy)
            return;

        _isBusy = true;
        button.IsEnabled = false;
        bool removing = _showingCurrentMedia;

        try
        {
            using var response = removing
                ? await _apiService.DeleteAsync($"api/playlists/{PlaylistId}/media/{media.MediaFileId}")
                : await _apiService.PostEmptyAsync($"api/playlists/{PlaylistId}/media/{media.MediaFileId}");

            // 404 on remove / 409 on add: an earlier attempt already
            // made this change, so just refresh the lists.
            bool alreadyDone = removing
                ? response.StatusCode == HttpStatusCode.NotFound
                : response.StatusCode == HttpStatusCode.Conflict;

            if (!response.IsSuccessStatusCode && !alreadyDone)
            {
                await ErrorPresenter.ShowAsync(this, response, "update the playlist");
                return;
            }

            await LoadAllMedia();
        }
        catch (Exception ex)
        {
            // Safe to try again: a repeat is reported as already done.
            await ErrorPresenter.ShowAsync(this, ex, "update the playlist", mayHaveSucceeded: true);
        }
        finally
        {
            button.IsEnabled = true;
            _isBusy = false;
        }
    }
}

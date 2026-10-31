

using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class ManagePlaylistMediaPage : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly HttpClient _httpClient;

    private List<MediaResponse> _currentMedia = new();
    private List<MediaResponse> _availableMedia = new();
    private bool _showingCurrentMedia = true;

    public string MediaActionText => _showingCurrentMedia ? "Remove" : "Add";
    public ManagePlaylistMediaPage()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
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

        await LoadCurrentMedia();
        await LoadAvailableMedia();

        UpdateMediaDisplay();
    }

    private async Task LoadCurrentMedia()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/playlists/{PlaylistId}/media");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }

        _currentMedia = await response.Content.ReadFromJsonAsync<List<MediaResponse>>() ?? new List<MediaResponse>();
    }

    private async Task LoadAvailableMedia()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/playlists/{PlaylistId}/available-media");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }

        _availableMedia = await response.Content.ReadFromJsonAsync<List<MediaResponse>>() ?? new List<MediaResponse>();
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
        if (sender is Button button && button.BindingContext is MediaResponse media)
            {

            button.IsEnabled = false;
            try
            {
                var token = await SecureStorage.Default.GetAsync("accessToken");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response;


                if (_showingCurrentMedia)
                {
                    response = await _httpClient.DeleteAsync($"api/playlists/{PlaylistId}/media/{media.MediaFileId}");
                } 
                
                else
                {
                response = await _httpClient.PostAsync($"api/playlists/{PlaylistId}/media/{media.MediaFileId}", null);
                }

                if (!response.IsSuccessStatusCode)
                    {
                        await DisplayAlertAsync("Error", "Something went wrong", "Close");
                        return;
                    }

                await LoadCurrentMedia();
                await LoadAvailableMedia();
                UpdateMediaDisplay();
                } 
            
            finally
            {
                button.IsEnabled = true;
            }

        }
    } 
}

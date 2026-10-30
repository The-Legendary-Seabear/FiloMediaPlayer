using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class EditPlaylistPage : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly HttpClient _httpClient;

    /*
    private List<ThumbnailResponse> _thumbnails = new();
    private int? _selectedThumbnailId;
    */
    private bool _isInitialized = false;
    public EditPlaylistPage()
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
        if(!_isInitialized)
        {

        await LoadPlaylist();
        await LoadThumbnails();

        _isInitialized = true;
        }
    }

    public async Task LoadPlaylist()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/playlists/{PlaylistId}");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }
        var playlist = await response.Content.ReadFromJsonAsync<PlaylistResponse>();

        if(playlist == null)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }

        PlaylistNameEntry.Text = playlist.Name;
        ThumbnailPicker.SelectedThumbnailId = playlist.ThumbnailId;
    }

    public async Task LoadThumbnails()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.GetAsync($"api/thumbnails");
        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }
        var thumbnails = await response.Content.ReadFromJsonAsync<List<ThumbnailResponse>>() ?? new List<ThumbnailResponse>(); ;
        ThumbnailPicker.Thumbnails = thumbnails;
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
        await DisplayAlertAsync("Debug","Save button clicked!","OK");

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

        var response = await _httpClient.PutAsJsonAsync($"api/playlists/{PlaylistId}", request);
        if(!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            return;
        }

        await DisplayAlertAsync("Success", "The Playlist has successfully been changed", "Close");
        await Shell.Current.GoToAsync("..");
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
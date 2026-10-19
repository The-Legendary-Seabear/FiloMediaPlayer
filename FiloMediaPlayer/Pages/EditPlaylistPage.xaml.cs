using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class EditPlaylistPage : ContentPage, IQueryAttributable
{
    public int PlaylistId { get; private set; }
    private readonly HttpClient _httpClient;

    private List<ThumbnailResponse> _thumbnails = new();
    private int? _selectedThumbnailId;
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

        await LoadPlaylist();
        await LoadThumbnails();
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
        SelectedThumbnailImage.Source = playlist.ThumbnailUrl;
        SelectedThumbnailName.Text = playlist.ThumbnailId != null ? "Thumbnail selected" : "No thumbnail selected";
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
        ThumbnailCollection.ItemsSource = thumbnails;
    }

    public async Task OnThumbnailSelected(object sender, EventArgs e)
    {
        if(sender is Button selected && selected.BindingContext is ThumbnailResponse thumbnail)
        {
            _selectedThumbnailId = thumbnail.ThumbnailId;
            SelectedThumbnailImage.Source = thumbnail.ImageUrl;
            SelectedThumbnailName.Text = thumbnail.Name;
        }
    }

    public async void OnSaveChangesClicked()
    {

    }
}
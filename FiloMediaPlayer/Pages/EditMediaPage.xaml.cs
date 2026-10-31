using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class EditMediaPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }
    private readonly HttpClient _httpClient;
    private bool _isInitialized = false;
    public EditMediaPage()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
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
        if (!_isInitialized)
        {

            await LoadMedia();
            await LoadThumbnails();

            _isInitialized = true;
        }
    }

    private async Task LoadMedia()
    {
        var token = await SecureStorage.Default.GetAsync("accessToken");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.GetAsync($"api/media/{MediaFileId}");

        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Failed to load media.", "Close");
            return;
        }

        var media = await response.Content.ReadFromJsonAsync<MediaResponse>();

        MediaTitleEntry.Text = media.Title;
        ThumbnailPicker.SelectedThumbnailId = media.ThumbnailId;
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

    public async void OnSaveChangesClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MediaTitleEntry.Text))
        {
            await DisplayAlertAsync("Error", "Please enter a valid media title.", "Close");
            return;
        }

        var request = new UpdateMediaRequest
        {
            Title = MediaTitleEntry.Text.Trim(),
            ThumbnailId = ThumbnailPicker.SelectedThumbnailId
        };

        var token = await SecureStorage.Default.GetAsync("accessToken");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.PutAsJsonAsync(
            $"api/media/{MediaFileId}", request);

        if (!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Failed to update media.", "Close");
            return;
        }

        await DisplayAlertAsync("Success", "Media updated successfully.", "Close");
        await Shell.Current.GoToAsync("..");
    }

    private async void OnManagePlaylistsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(
            "ManageMediaPlaylistsPage",
            new Dictionary<string, object>
            {
            { "MediaFileId", MediaFileId }
            });
    }
}
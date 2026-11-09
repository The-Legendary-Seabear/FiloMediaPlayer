using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class EditMediaPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }
    private readonly HttpClient _httpClient;
    private bool _isInitialized = false;

    private CancellationTokenSource? _conversionCts;
    private int? _conversionJobId;
    private string _mediaContentType = string.Empty;

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


        if (media is null)
        {
            await DisplayAlertAsync(
                "Error",
                "The API returned invalid media information.",
                "Close");

            return;
        }

        MediaTitleEntry.Text = media.Title;
        ThumbnailPicker.SelectedThumbnailId = media.ThumbnailId;



        _mediaContentType = media.ContentType ?? string.Empty;

        ConfigureConversionFormats();
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


    private void OnConversionFormatChanged(
        object sender,
        EventArgs e)
    {
        if (ConversionFormatPicker.SelectedIndex < 0)
            return;

        var format = ConversionFormatPicker.SelectedItem
            .ToString()!
            .ToLowerInvariant();

        VideoCodecPicker.Items.Clear();

        switch (format)
        {
            case "mp4":
            case "mkv":
                VideoCodecPicker.Items.Add("h264");
                break;

            case "webm":
                VideoCodecPicker.Items.Add("vp8");
                VideoCodecPicker.Items.Add("vp9");
                break;
        }

        VideoCodecSection.IsVisible =
            VideoCodecPicker.Items.Count > 0;

        if (VideoCodecSection.IsVisible)
        {
            VideoCodecPicker.SelectedIndex =
                format == "webm" ? 1 : 0;
        }
        else
        {
            VideoCodecPicker.SelectedIndex = -1;
        }
    }

    private async void OnConvertMediaClicked(
        object sender,
        EventArgs e)
    {
        if (MediaFileId <= 0)
        {
            await DisplayAlertAsync(
                "Error",
                "No media file was selected.",
                "Close");
            return;
        }

        if (ConversionFormatPicker.SelectedIndex < 0)
        {
            await DisplayAlertAsync(
                "Choose format",
                "Select an output format first.",
                "Close");
            return;
        }

        var format = ConversionFormatPicker.SelectedItem
            .ToString()!
            .ToLowerInvariant();

        string? codec = VideoCodecSection.IsVisible
            ? VideoCodecPicker.SelectedItem?.ToString()
            : null;

        ConvertMediaButton.IsEnabled = false;
        ConversionStatusLabel.Text = "Submitting conversion...";
        ConversionActivityIndicator.IsVisible = true;
        ConversionActivityIndicator.IsRunning = true;

        try
        {
            var token = await SecureStorage.Default
                .GetAsync("accessToken");

            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "You must be signed in to convert media.");

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var request = new
            {
                MediaFileId,
                TargetFormat = format,
                VideoCodec = codec
            };

            var response = await _httpClient.PostAsJsonAsync(
                "api/processing-jobs/conversion",
                request);

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();

                throw new HttpRequestException(
                    $"Unable to queue conversion: {message}");
            }

            var result = await response.Content
                .ReadFromJsonAsync<System.Text.Json.JsonElement>();

            _conversionJobId =
                result.GetProperty("processingJobId").GetInt32();

            ConversionStatusLabel.Text =
                $"Job {_conversionJobId}: Queued";

            _conversionCts?.Cancel();
            _conversionCts = new CancellationTokenSource();

            await MonitorConversionAsync(
                _conversionJobId.Value,
                _conversionCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Leaving the page stops monitoring, not the server job.
        }
        catch (Exception ex)
        {
            ConversionStatusLabel.Text = "Unable to start or monitor conversion.";

            await DisplayAlertAsync(
                "Conversion error",
                ex.Message,
                "Close");
        }
        finally
        {
            ConvertMediaButton.IsEnabled = true;
            ConversionActivityIndicator.IsRunning = false;
            ConversionActivityIndicator.IsVisible = false;
        }
    }



    private async Task MonitorConversionAsync(
        int jobId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(3000, cancellationToken);

            var response = await _httpClient.GetAsync(
                $"api/processing-jobs/{jobId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    "Unable to retrieve conversion status.");
            }

            var job = await response.Content
                .ReadFromJsonAsync<System.Text.Json.JsonElement>(
                    cancellationToken: cancellationToken);

            var statusElement = job.GetProperty("status");

            // Handles string enum values or the default numeric
            // serialization of ProcessingJobStatus.
            string status;

            if (statusElement.ValueKind ==
                System.Text.Json.JsonValueKind.String)
            {
                status = statusElement.GetString() ?? "Unknown";
            }
            else
            {
                var statusCode = statusElement.GetInt32();

                status = statusCode switch
                {
                    0 => "Queued",
                    1 => "Processing",
                    2 => "Completed",
                    3 => "Failed",
                    _ => $"Unknown ({statusCode})"
                };
            }

            ConversionStatusLabel.Text =
                $"Job {jobId}: {status}";

            if (status == "Completed")
            {
                ConversionStatusLabel.Text =
                    "Conversion completed! The converted file has been added to your media library.";

                return;
            }

            if (status == "Failed")
            {
                string errorMessage = "Conversion failed.";

                if (job.TryGetProperty("errorMessage", out var error) &&
                    error.ValueKind ==
                    System.Text.Json.JsonValueKind.String)
                {
                    errorMessage = error.GetString()
                        ?? errorMessage;
                }

                ConversionStatusLabel.Text = errorMessage;
                return;
            }
        }
    }



    protected override void OnDisappearing()
    {
        _conversionCts?.Cancel();
        base.OnDisappearing();
    }


    private void ConfigureConversionFormats()
    {
        ConversionFormatPicker.Items.Clear();

        bool isAudio = _mediaContentType.StartsWith(
            "audio/",
            StringComparison.OrdinalIgnoreCase);

        bool isVideo = _mediaContentType.StartsWith(
            "video/",
            StringComparison.OrdinalIgnoreCase);

        if (!isAudio && !isVideo)
        {
            ConvertMediaButton.IsEnabled = false;
            ConversionStatusLabel.Text =
                "This media type cannot be converted.";
            return;
        }

        ConversionFormatPicker.Items.Add("MP3");
        ConversionFormatPicker.Items.Add("FLAC");
        ConversionFormatPicker.Items.Add("WAV");

        if (isVideo)
        {
            ConversionFormatPicker.Items.Add("MP4");
            ConversionFormatPicker.Items.Add("WebM");
            ConversionFormatPicker.Items.Add("MKV");
        }

        ConversionFormatPicker.SelectedIndex = -1;

        VideoCodecPicker.Items.Clear();
        VideoCodecSection.IsVisible = false;

        ConvertMediaButton.IsEnabled = true;
    }


}
using FiloMediaPlayer.DTOs;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class EditMediaPage : ContentPage, IQueryAttributable
{
    public int MediaFileId { get; private set; }
    private readonly FiloApiService _apiService;
    private bool _mediaLoaded;
    private bool _thumbnailsLoaded;
    private bool _isLoading;
    private bool _isSaving;

    private CancellationTokenSource? _conversionCts;
    private int? _conversionJobId;
    private string _mediaContentType = string.Empty;

    // Consecutive failed status checks tolerated before monitoring stops.
    private const int MaxMonitorFailures = 10;

    public EditMediaPage(FiloApiService apiService)
	{
		InitializeComponent();
        _apiService = apiService;
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

        // A failed load is retried the next time the page appears.
        // Each part loads once so a retry never overwrites the user's edits.
        if ((!_mediaLoaded || !_thumbnailsLoaded) && !_isLoading)
        {
            _isLoading = true;

            try
            {
                if (!_mediaLoaded)
                    _mediaLoaded = await LoadMedia();

                if (_mediaLoaded && !_thumbnailsLoaded)
                    _thumbnailsLoaded = await LoadThumbnails();
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    private async Task<bool> LoadMedia()
    {
        while (true)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync($"api/media/{MediaFileId}");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(this, response, "load the media"))
                        continue;

                    return false;
                }

                var media = await response.Content.ReadFromJsonAsync<MediaResponse>();

                if (media is null)
                {
                    await DisplayAlertAsync(
                        "Error",
                        "The API returned invalid media information.",
                        "Close");

                    return false;
                }

                MediaTitleEntry.Text = media.Title;
                ThumbnailPicker.SelectedThumbnailId = media.ThumbnailId;

                _mediaContentType = media.ContentType ?? string.Empty;

                ConfigureConversionFormats();
                return true;
            }
            catch (Exception ex)
            {
                if (await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load the media"))
                    continue;

                return false;
            }
        }
    }

    public async Task<bool> LoadThumbnails()
    {
        while (true)
        {
            try
            {
                using var response = await _apiService.GetWithRetryAsync("api/thumbnails");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(this, response, "load your thumbnails"))
                        continue;

                    return false;
                }

                var thumbnails = await response.Content.ReadFromJsonAsync<List<ThumbnailResponse>>() ?? new List<ThumbnailResponse>();
                ThumbnailPicker.Thumbnails = thumbnails;
                return true;
            }
            catch (Exception ex)
            {
                if (await ErrorPresenter.ShowLoadFailureAsync(this, ex, "load your thumbnails"))
                    continue;

                return false;
            }
        }
    }

    public async void OnSaveChangesClicked(object sender, EventArgs e)
    {
        if (_isSaving)
            return;

        // Saving before the media loaded would overwrite it with blanks.
        if (!_mediaLoaded)
        {
            await DisplayAlertAsync(
                "Not Loaded",
                "The media hasn't loaded yet, so it can't be saved. Go back and open it again.",
                "Close");
            return;
        }

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

        _isSaving = true;

        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            using var response = await _apiService.PutAsync(
                $"api/media/{MediaFileId}", request);

            if (!response.IsSuccessStatusCode)
            {
                await ErrorPresenter.ShowAsync(this, response, "update the media");
                return;
            }

            await DisplayAlertAsync("Success", "Media updated successfully.", "Close");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            // The form keeps the user's changes so they can save again.
            await ErrorPresenter.ShowAsync(this, ex, "update the media", mayHaveSucceeded: true);
        }
        finally
        {
            _isSaving = false;

            if (sender is Button finishedButton)
                finishedButton.IsEnabled = true;
        }
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

        _conversionJobId = null;

        try
        {
            var request = new
            {
                MediaFileId,
                TargetFormat = format,
                VideoCodec = codec
            };

            using var response = await _apiService.PostAsync(
                "api/processing-jobs/conversion",
                request);

            if (!response.IsSuccessStatusCode)
            {
                ConversionStatusLabel.Text = "Unable to start the conversion.";
                await ErrorPresenter.ShowAsync(this, response, "start the conversion");
                return;
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
        catch (OperationCanceledException ex) when (!ApiErrors.IsTimeout(ex))
        {
            // Leaving the page stops monitoring, not the server job.
        }
        catch (Exception ex)
        {
            if (_conversionJobId.HasValue && ApiErrors.IsConnectionFailure(ex))
            {
                // Only monitoring failed; the job keeps running on the server.
                ConversionStatusLabel.Text =
                    $"Lost connection while checking job {_conversionJobId}. " +
                    "The conversion continues on the server; check your library later.";
            }
            else
            {
                ConversionStatusLabel.Text = "Unable to start or monitor conversion.";

                // A failed submit may still have queued the job.
                await ErrorPresenter.ShowAsync(
                    this, ex, "start the conversion",
                    mayHaveSucceeded: !_conversionJobId.HasValue);
            }
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
        int consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(3000, cancellationToken);

            System.Text.Json.JsonElement job;

            try
            {
                using var response = await _apiService.GetWithRetryAsync(
                    $"api/processing-jobs/{jobId}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        "Unable to retrieve conversion status.",
                        null,
                        response.StatusCode);
                }

                job = await response.Content
                    .ReadFromJsonAsync<System.Text.Json.JsonElement>(
                        cancellationToken: cancellationToken);

                consecutiveFailures = 0;
            }
            catch (Exception ex) when (
                ApiErrors.IsConnectionFailure(ex) &&
                ++consecutiveFailures < MaxMonitorFailures)
            {
                // Brief outage: keep checking the same job.
                ConversionStatusLabel.Text =
                    $"Job {jobId}: Connection lost, reconnecting...";
                continue;
            }

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
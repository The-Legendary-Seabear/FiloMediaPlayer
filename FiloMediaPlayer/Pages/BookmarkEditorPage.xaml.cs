
using FiloMediaPlayer.Models.Bookmarks;
using FiloMediaPlayer.Services;
using Microsoft.Maui.Graphics;
using System.Diagnostics;
using System.Globalization;

namespace FiloMediaPlayer.Pages;

public partial class BookmarkEditorPage : ContentView
{
    private bool _isSection = false;
    private string? _selectedColor = null;

    private readonly int _mediaFileId;
    private readonly double _mediaDuration;
    private readonly FiloApiService _apiService;

    // PlaybackPage supplies the actual current media position.
    public Func<double>? PlaybackPositionProvider { get; set; }

    // Notify PlaybackPage when the editor should close or refresh.
    public event EventHandler? CancelRequested;
    public event EventHandler? BookmarkSaved;

    private readonly int? _editingBookmarkId;


    public BookmarkEditorPage(
        FiloApiService apiService,
        int mediaFileId,
        double currentPosition,
        double mediaDuration,
        BookmarkResponse? bookmark = null)
    {
        InitializeComponent();

        _apiService = apiService;
        _mediaFileId = mediaFileId;
        _mediaDuration = mediaDuration;

        PlaybackPositionProvider = null;

        UpdateCurrentPosition(currentPosition);

        if (bookmark != null)
        {
            // Editing an existing bookmark
            _editingBookmarkId = bookmark.MediaBookmarkId;

            EditorTitleLabel.Text = "Edit Bookmark";
            SaveBookmarkButton.Text = "Save Changes";

            BookmarkTitleEntry.Text = bookmark.Title;
            BookmarkDescriptionEditor.Text = bookmark.Description;

            StartTimeEntry.Text =
                FormatTimestamp(bookmark.StartSeconds);

            _isSection = bookmark.EndSeconds.HasValue;

            if (_isSection)
            {
                EndTimeEntry.Text =
                    FormatTimestamp(bookmark.EndSeconds!.Value);
            }

            _selectedColor = bookmark.Color;

            if (!string.IsNullOrWhiteSpace(_selectedColor))
            {
                BookmarkColorPreview.BackgroundColor =
                    Color.FromArgb(_selectedColor);

                BookmarkColorHexLabel.Text = _selectedColor;
            }
            else
            {
                BookmarkColorPreview.BackgroundColor =
                    Color.FromArgb("#8B5CF6");

                BookmarkColorHexLabel.Text = "Default";
            }
        }
        else
        {
            // Creating a new bookmark
            _editingBookmarkId = null;

            EditorTitleLabel.Text = "Add Bookmark";
            SaveBookmarkButton.Text = "Save Bookmark";

            StartTimeEntry.Text = FormatTimestamp(currentPosition);
        }

        UpdateBookmarkType();
    }


    // Format seconds for display.
    private static string FormatTimestamp(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;

        return TimeSpan.FromSeconds(Math.Floor(seconds))
            .ToString(@"hh\:mm\:ss");
    }

    // Called by PlaybackPage as the player moves.
    public void UpdateCurrentPosition(double seconds)
    {
        CurrentPositionLabel.Text = FormatTimestamp(seconds);
    }

    // Capture the current player position.
    private double GetCurrentPosition()
    {
        double position = PlaybackPositionProvider?.Invoke() ?? 0;

        if (!double.IsFinite(position))
            return 0;

        if (_mediaDuration > 0)
            position = Math.Min(position, _mediaDuration);

        return Math.Max(0, position);
    }

    private void OnSetStartClicked(object sender, EventArgs e)
    {
        StartTimeEntry.Text =
            FormatTimestamp(GetCurrentPosition());
    }

    private void OnSetEndClicked(object sender, EventArgs e)
    {
        EndTimeEntry.Text =
            FormatTimestamp(GetCurrentPosition());
    }

    private void OnPointBookmarkClicked(object sender, EventArgs e)
    {
        _isSection = false;
        UpdateBookmarkType();
    }

    private void OnSectionBookmarkClicked(object sender, EventArgs e)
    {
        _isSection = true;
        UpdateBookmarkType();
    }

    private void UpdateBookmarkType()
    {
        EndTimeContainer.IsVisible = _isSection;

        PointBookmarkButton.Opacity = _isSection ? 0.6 : 1;
        SectionBookmarkButton.Opacity = _isSection ? 1 : 0.6;
    }

    private async void OnChooseColorClicked(object sender, EventArgs e)
    {
        var colorPicker = new ColorPickerPage();

        colorPicker.Disappearing += (_, _) =>
        {
            if (colorPicker.SelectedColor == null)
                return;

            _selectedColor = colorPicker.SelectedColor;

            BookmarkColorPreview.BackgroundColor =
                Color.FromArgb(_selectedColor);

            BookmarkColorHexLabel.Text = _selectedColor;
        };

        await Navigation.PushModalAsync(colorPicker);
    }

    private void OnResetColorClicked(object sender, EventArgs e)
    {
        _selectedColor = null;

        BookmarkColorPreview.BackgroundColor =
            Color.FromArgb("#8B5CF6");

        BookmarkColorHexLabel.Text = "Default";
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowEditorAlertAsync(
        string title,
        string message)
    {
        Page? page = Window?.Page ?? Shell.Current;

        if (page != null)
        {
            await page.DisplayAlertAsync(title, message, "OK");
        }
    }

    private async void OnSaveBookmarkClicked(object sender, EventArgs e)
    {
        string title = BookmarkTitleEntry.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(title))
        {
            await ShowEditorAlertAsync(
                "Invalid Bookmark",
                "Please enter a bookmark title.");
            return;
        }

        if (!TryParseTimestamp(
            StartTimeEntry.Text, out double startSeconds))
        {
            await ShowEditorAlertAsync(
                "Invalid Timestamp",
                "Enter a valid start time.");
            return;
        }

        double? endSeconds = null;

        if (_isSection)
        {
            if (!TryParseTimestamp(
                EndTimeEntry.Text, out double end))
            {
                await ShowEditorAlertAsync(
                    "Invalid Timestamp",
                    "Enter a valid section end time.");
                return;
            }

            if (end <= startSeconds)
            {
                await ShowEditorAlertAsync(
                    "Invalid Section",
                    "The end time must be after the start time.");
                return;
            }

            endSeconds = end;
        }

        if (_mediaDuration > 0 &&
            (startSeconds > _mediaDuration ||
             (endSeconds.HasValue &&
              endSeconds.Value > _mediaDuration)))
        {
            await ShowEditorAlertAsync(
                "Invalid Timestamp",
                "The bookmark exceeds the media duration.");
            return;
        }


        SaveBookmarkButton.IsEnabled = false;

        try
        {
            HttpResponseMessage response;

            if (_editingBookmarkId.HasValue)
            {
                var updateRequest = new UpdateBookmarkRequest
                {
                    Title = title,
                    Description = BookmarkDescriptionEditor.Text,
                    StartSeconds = startSeconds,
                    EndSeconds = endSeconds,
                    Color = _selectedColor
                };

                response = await _apiService.PutAsync(
                    $"api/media-bookmarks/{_editingBookmarkId.Value}",
                    updateRequest);
            }
            else
            {
                var createRequest = new CreateBookmarkRequest
                {
                    MediaFileId = _mediaFileId,
                    Title = title,
                    Description = BookmarkDescriptionEditor.Text,
                    StartSeconds = startSeconds,
                    EndSeconds = endSeconds,
                    Color = _selectedColor
                };

                response = await _apiService.PostAsync(
                    "api/media-bookmarks",
                    createRequest);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string error =
                        await response.Content.ReadAsStringAsync();

                    await ShowEditorAlertAsync(
                        "Bookmark Error",
                        $"Unable to save bookmark.\n{error}");

                    return;
                }
            }

            // PlaybackPage closes the editor and refreshes the list.
            BookmarkSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            await ShowEditorAlertAsync(
                "Session Expired",
                "Please log in again.");
        }
        catch (HttpRequestException)
        {
            await ShowEditorAlertAsync(
                "Connection Error",
                "Unable to contact the server.");
        }
        catch (TaskCanceledException)
        {
            await ShowEditorAlertAsync(
                "Timeout",
                "The bookmark request timed out.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bookmark save error: {ex}");

            await ShowEditorAlertAsync(
                "Error",
                "An unexpected error occurred while saving.");
        }
        finally
        {
            SaveBookmarkButton.IsEnabled = true;
        }
    }

    private static bool TryParseTimestamp(
        string? input,
        out double seconds)
    {
        seconds = 0;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var formats = new[]
        {
            @"m\:ss",
            @"mm\:ss",
            @"h\:mm\:ss",
            @"hh\:mm\:ss"
        };

        if (!TimeSpan.TryParseExact(
            input.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            out var time))
        {
            return false;
        }

        seconds = time.TotalSeconds;

        return seconds >= 0 && double.IsFinite(seconds);
    }
}

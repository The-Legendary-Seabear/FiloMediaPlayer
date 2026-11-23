
using FiloMediaPlayer.DTOs;
using System.Collections.ObjectModel;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class MediaGraftingPage : ContentPage
{
    private readonly FiloApiService _apiService;

    private List<MediaResponse> _allMedia = new();

    private readonly ObservableCollection<MediaResponse>
        _selectedMedia = new();

    private MediaResponse? _draggedMedia;
    private bool _isSubmittingGraft;

    private bool _loaded;
    private bool _isLoading;

    public MediaGraftingPage(FiloApiService apiService)
    {
        InitializeComponent();

        _apiService = apiService;

        SelectedMediaCollection.ItemsSource = _selectedMedia;

        GraftingTypePicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // A failed load is retried the next time the page appears.
        if (_loaded || _isLoading)
            return;

        _isLoading = true;

        try
        {
            await LoadMedia();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadMedia()
    {
        while (true)
        {
            try
            {
                using var response =
                    await _apiService.GetWithRetryAsync("api/media");

                if (!response.IsSuccessStatusCode)
                {
                    if (await ErrorPresenter.ShowLoadFailureAsync(
                        this, response, "load your media library"))
                        continue;

                    return;
                }

                _allMedia = await response.Content
                    .ReadFromJsonAsync<List<MediaResponse>>()
                    ?? new();

                _loaded = true;

                UpdateAvailableMedia();
                return;
            }
            catch (Exception ex)
            {
                if (!await ErrorPresenter.ShowLoadFailureAsync(
                    this, ex, "load your media library"))
                    return;
            }
        }
    }

    private void UpdateAvailableMedia()
    {
        bool audioMode =
            GraftingTypePicker.SelectedIndex == 0;

        string search =
            MediaSearchBar.Text?.Trim() ?? "";

        var available = _allMedia
            .Where(m =>
                (audioMode
                    ? m.ContentType?.StartsWith(
                        "audio/",
                        StringComparison.OrdinalIgnoreCase) == true
                    : m.ContentType?.StartsWith(
                        "video/",
                        StringComparison.OrdinalIgnoreCase) == true))
            .Where(m =>
                m.Title.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            .Where(m =>
                !_selectedMedia.Any(
                    s => s.MediaFileId == m.MediaFileId))
            .ToList();

        AvailableMediaCollection.ItemsSource = available;
    }

    private void OnMediaSearchChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateAvailableMedia();
    }

    private void OnGraftingTypeChanged(
        object sender,
        EventArgs e)
    {
        _selectedMedia.Clear();
        _draggedMedia = null;

        UpdateAvailableMedia();
        UpdateCreateButton();
    }

    private void OnAddMediaClicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext is not MediaResponse media)
            return;

        if (_selectedMedia.Any(
            m => m.MediaFileId == media.MediaFileId))
            return;

        _selectedMedia.Add(media);

        UpdateAvailableMedia();
        UpdateCreateButton();
    }

    private void OnRemoveMediaClicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext is not MediaResponse media)
            return;

        _selectedMedia.Remove(media);

        UpdateAvailableMedia();
        UpdateCreateButton();
    }

    private void OnMoveUpClicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext is not MediaResponse media)
            return;

        int index = _selectedMedia.IndexOf(media);

        if (index > 0)
        {
            _selectedMedia.Move(index, index - 1);
        }
    }

    private void OnMoveDownClicked(
        object sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext is not MediaResponse media)
            return;

        int index = _selectedMedia.IndexOf(media);

        if (index >= 0 &&
            index < _selectedMedia.Count - 1)
        {
            _selectedMedia.Move(index, index + 1);
        }
    }

    private void OnMediaDragStarting(
        object sender,
        DragStartingEventArgs e)
    {
        if (sender is DragGestureRecognizer gesture &&
            gesture.BindingContext is MediaResponse media)
        {
            _draggedMedia = media;

            e.Data.Text = media.MediaFileId.ToString();
        }
    }

    private void OnMediaDropped(
        object sender,
        DropEventArgs e)
    {
        if (_draggedMedia == null)
            return;

        if (sender is not DropGestureRecognizer gesture ||
            gesture.BindingContext is not MediaResponse target)
            return;

        int oldIndex = _selectedMedia.IndexOf(_draggedMedia);
        int newIndex = _selectedMedia.IndexOf(target);

        if (oldIndex < 0 || newIndex < 0)
            return;

        if (oldIndex != newIndex)
        {
            _selectedMedia.Move(oldIndex, newIndex);
        }

        _draggedMedia = null;
    }


    private async void OnCreateGraftClicked(
        object sender,
        EventArgs e)
    {
        if (_isSubmittingGraft ||
            _selectedMedia.Count < 2 ||
            string.IsNullOrWhiteSpace(OutputTitleEntry.Text))
        {
            return;
        }

        _isSubmittingGraft = true;
        UpdateCreateButton();

        try
        {
            var request = new
            {
                Title = OutputTitleEntry.Text.Trim(),

                MediaType =
                    GraftingTypePicker.SelectedIndex == 0
                        ? "audio"
                        : "video",

                MediaFileIds = _selectedMedia
                    .Select(m => m.MediaFileId)
                    .ToList()
            };

            using var response = await _apiService.PostAsync(
                "api/processing-jobs/grafting",
                request);

            if (!response.IsSuccessStatusCode)
            {
                await ErrorPresenter.ShowAsync(
                    this, response, "create the grafting job");

                return;
            }

            await DisplayAlertAsync(
                "Grafting Job Created",
                "Your media grafting job has been queued.",
                "OK");

            _selectedMedia.Clear();
            OutputTitleEntry.Text = string.Empty;
            UpdateAvailableMedia();
        }
        catch (Exception ex)
        {
            // Not retried automatically: the job may already be queued.
            // The selection is kept so the user can submit again.
            await ErrorPresenter.ShowAsync(
                this, ex, "create the grafting job", mayHaveSucceeded: true);
        }
        finally
        {
            _isSubmittingGraft = false;
            UpdateCreateButton();
        }
    }


    private void UpdateCreateButton()
    {
        bool hasEnoughMedia = _selectedMedia.Count >= 2;

        bool hasTitle = !string.IsNullOrWhiteSpace(
            OutputTitleEntry.Text);

        CreateGraftButton.IsEnabled =
            hasEnoughMedia &&
            hasTitle &&
            !_isSubmittingGraft;
    }

    private void OnOutputTitleChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateCreateButton();
    }
}

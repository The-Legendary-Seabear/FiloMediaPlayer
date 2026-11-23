namespace FiloMediaPlayer.Pages;

public partial class UploadThumbnailPage : ContentPage
{

    private FileResult? _selectedFile;
    private readonly FiloApiService _apiService;
    private string defaultFileNamePlaceHolder = "No File Selected";
    private bool _isUploading;


    public UploadThumbnailPage(FiloApiService apiService)
	{
		InitializeComponent();
        _apiService = apiService;
    }

	private async void OnUploadThumbnailButton(object sender, EventArgs e)
	{
        if (_isUploading)
            return;

        var options = new PickOptions {
            PickerTitle = "Select a thumbnail image",
            FileTypes = FilePickerFileType.Images
        };

        var filePicked = await FilePicker.Default.PickAsync(options);
        if (filePicked == null)
        {
            await DisplayAlertAsync("File", "Please Give a valid file", "Cancel");
            return;
        }

        _selectedFile = filePicked;
        SelectedThumbnailName.Text = _selectedFile.FileName;
    }

    private async void OnFinalThumbnailButton(object sender, EventArgs e)
    {
        if (_isUploading)
            return;

        if (string.IsNullOrWhiteSpace(UserUploadedThumbnailName.Text))
        {
            await DisplayAlertAsync("Thumbnail Name", "Please Select a valid thumbnail Name", "Close");
            return;
        }

        if (_selectedFile == null)
        {
            await DisplayAlertAsync("File", "Please Give a valid file", "Cancel");
            return;
        }

        _isUploading = true;
        UploadThumbnailButton.IsEnabled = false;
        UploadStatusPanel.IsVisible = true;
        UploadActivityIndicator.IsRunning = true;

        var file = _selectedFile;
        string name = UserUploadedThumbnailName.Text;

        try
        {
            using var response = await _apiService.PostMultipartAsync(
                "api/thumbnails/upload",
                async () =>
                {
                    var form = new MultipartFormDataContent();
                    form.Add(new StreamContent(await file.OpenReadAsync()), "file", file.FileName);
                    form.Add(new StringContent(name), "name");
                    return form;
                });

            if (!response.IsSuccessStatusCode)
            {
                // Keep the selection so the user can try again.
                await ErrorPresenter.ShowAsync(this, response, "upload the thumbnail");
                return;
            }

            await DisplayAlertAsync("Media Upload", "Finished uploading thumbnail", "Close");
            _selectedFile = null;
            UserUploadedThumbnailName.Text = string.Empty;
            SelectedThumbnailName.Text = defaultFileNamePlaceHolder;
        }
        catch (Exception ex)
        {
            await ErrorPresenter.ShowAsync(
                this, ex, "upload the thumbnail", mayHaveSucceeded: true);
        }
        finally
        {
            _isUploading = false;
            UploadActivityIndicator.IsRunning = false;
            UploadStatusPanel.IsVisible = false;
            UploadThumbnailButton.IsEnabled = true;
        }
    }
}

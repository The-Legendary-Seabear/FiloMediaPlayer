using Microsoft.Maui.Storage;

namespace FiloMediaPlayer.Pages;

public partial class UploadMediaPage : ContentPage
{
    private FileResult? _selectedFile;
    private readonly FiloApiService _apiService;
	private string defaultFileNamePlaceHolder = "No File Selected";
    private bool _isUploading;

    public UploadMediaPage(FiloApiService apiService)
	{
		InitializeComponent();
        _apiService = apiService;
    }

	private async void OnUploadMediaButton(object sender, EventArgs e)
	{
        if (_isUploading)
            return;

		var filePicked = await FilePicker.Default.PickAsync();
		if(filePicked == null)
		{
			await DisplayAlertAsync("File", "Please Give a valid file", "Cancel");
			return;
		}

		_selectedFile = filePicked;
		SelectedMediaName.Text = _selectedFile.FileName;


    }

	private async void OnFinalMediaButton(object sender, EventArgs e)
	{
        if (_isUploading)
            return;

        if (string.IsNullOrEmpty(UserUploadedMediaName.Text))
		{
			await DisplayAlertAsync("Media Name", "Please Select a valid Media Name", "Close");
			return;
		}

        if (_selectedFile == null)
        {
            await DisplayAlertAsync("File", "Please Give a valid file", "Cancel");
            return;
        }

        _isUploading = true;
		UploadMediaButton.IsEnabled = false;
        UploadStatusPanel.IsVisible = true;
        UploadActivityIndicator.IsRunning = true;

        var file = _selectedFile;
        string name = UserUploadedMediaName.Text;

        try
        {
            using var response = await _apiService.PostMultipartAsync(
                "api/media/upload",
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
                await ErrorPresenter.ShowAsync(this, response, "upload the media");
                return;
            }

            await DisplayAlertAsync("Media Upload", "Finished uploading media", "Close");
            _selectedFile = null;
            UserUploadedMediaName.Text = string.Empty;
            SelectedMediaName.Text = defaultFileNamePlaceHolder;
        }
        catch (Exception ex)
        {
            await ErrorPresenter.ShowAsync(
                this, ex, "upload the media", mayHaveSucceeded: true);
        }
        finally
        {
            _isUploading = false;
            UploadActivityIndicator.IsRunning = false;
            UploadStatusPanel.IsVisible = false;
            UploadMediaButton.IsEnabled = true;
        }
    }

}

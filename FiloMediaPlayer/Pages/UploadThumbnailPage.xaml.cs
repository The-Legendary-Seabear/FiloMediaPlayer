using System.Net.Http.Headers;

namespace FiloMediaPlayer.Pages;

public partial class UploadThumbnailPage : ContentPage
{

    private FileResult? _selectedFile;
    private readonly HttpClient _httpClient;
    private string defaultFileNamePlaceHolder = "No File Selected";


    public UploadThumbnailPage()
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
    }

	private async void OnUploadThumbnailButton(object sender, EventArgs e)
	{
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
        UploadThumbnailButton.IsEnabled = false;
        UploadStatusPanel.IsVisible = true;
        UploadActivityIndicator.IsRunning = true;

        var stream = await _selectedFile.OpenReadAsync();
        var fileContent = new StreamContent(stream);
        var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", _selectedFile.FileName);
        form.Add(new StringContent(UserUploadedThumbnailName.Text), "name");

        var token = await SecureStorage.Default.GetAsync("accessToken");
        if (string.IsNullOrEmpty(token))
        {
            UploadActivityIndicator.IsRunning = false;
            UploadStatusPanel.IsVisible = false;
            UploadThumbnailButton.IsEnabled = true;
            await DisplayAlertAsync("Token Access", "Could not retrieve the access token", "Close");
            return;
        }


        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.PostAsync("api/thumbnails/upload", form);
        UploadActivityIndicator.IsRunning = false;
        UploadStatusPanel.IsVisible = false;

        if(!response.IsSuccessStatusCode)
        {
            await DisplayAlertAsync("Error", "Something went wrong", "Close");
            UploadThumbnailButton.IsEnabled = true;
            return;
        }

        await DisplayAlertAsync("Media Upload", "Finished uploading thumbnail", "Close");
        _selectedFile = null;
        UserUploadedThumbnailName.Text = string.Empty;
        SelectedThumbnailName.Text = defaultFileNamePlaceHolder;
        UploadThumbnailButton.IsEnabled = true;
    }
}
using Microsoft.Maui.Storage;
using System.Net.Http.Headers;

namespace FiloMediaPlayer.Pages;

public partial class UploadMediaPage : ContentPage
{
    private FileResult? _selectedFile;
    private readonly HttpClient _httpClient;
	private string defaultFileNamePlaceHolder = "No File Selected";
    public UploadMediaPage()
	{
		InitializeComponent();
		_httpClient = new HttpClient();
		_httpClient.BaseAddress = new Uri("https://localhost:7038/");

    }

	private async void OnUploadMediaButton(object sender, EventArgs e)
	{
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
		UploadMediaButton.IsEnabled = false;
        UploadStatusPanel.IsVisible = true;
        UploadActivityIndicator.IsRunning = true;

		var stream = await _selectedFile.OpenReadAsync();
        var fileContent = new StreamContent(stream);
        var form = new MultipartFormDataContent();
		form.Add(fileContent, "file", _selectedFile.FileName);
		form.Add(new StringContent(UserUploadedMediaName.Text), "name");

		var token = await SecureStorage.Default.GetAsync("accessToken");
		if(string.IsNullOrEmpty(token))
		{
            UploadActivityIndicator.IsRunning = false;
            UploadStatusPanel.IsVisible = false;
            UploadMediaButton.IsEnabled = true;
            await DisplayAlertAsync("Token Access", "Could not retrieve the access token", "Close");
			return;
		}


        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var response = await _httpClient.PostAsync("api/media/upload", form);
        UploadActivityIndicator.IsRunning = false;
        UploadStatusPanel.IsVisible = false;
		await DisplayAlertAsync("Media Upload", "Finished uploading media", "Close");
		_selectedFile = null;
		UserUploadedMediaName.Text = string.Empty;
		SelectedMediaName.Text = defaultFileNamePlaceHolder;
        UploadMediaButton.IsEnabled = true;
    }

}
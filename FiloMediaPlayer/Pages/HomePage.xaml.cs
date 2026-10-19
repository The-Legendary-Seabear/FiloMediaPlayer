namespace FiloMediaPlayer.Pages;

public partial class HomePage : ContentPage
{
	public HomePage()
	{
		InitializeComponent();
	}

	private async void OnCreatePlaylistButton(object sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("CreatePlaylistPage");
	}

    private async void OnShowPlaylistButton(object sender, EventArgs e)
	{
        await Shell.Current.GoToAsync("ShowPlaylistsPage");
    }

    private async void OnAddMediaFileButton(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("UploadMediaPage");
    }

	private async void OnCreateThumbnailButton(object sender, EventArgs e)
	{
        await Shell.Current.GoToAsync("UploadThumbnailPage");
    }
}
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
}
namespace FiloMediaPlayer;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("HomePage", typeof(HomePage));
        Routing.RegisterRoute("CreatePlaylistPage", typeof(CreatePlaylistPage));
        Routing.RegisterRoute("ShowPlaylistsPage", typeof(ShowPlaylistsPage));
        Routing.RegisterRoute("UploadMediaPage", typeof(UploadMediaPage));
        Routing.RegisterRoute("ShowPlaylistSongs", typeof(ShowPlaylistSongs));
        Routing.RegisterRoute("UploadThumbnailPage", typeof(UploadThumbnailPage));
        Routing.RegisterRoute("EditPlaylistPage", typeof(EditPlaylistPage));
    }
}
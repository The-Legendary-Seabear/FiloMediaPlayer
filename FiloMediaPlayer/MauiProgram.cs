using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using FiloMediaPlayer.Services;
using FiloMediaPlayer.Pages;

namespace FiloMediaPlayer
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkitMediaElement(false)
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    // The alias must match Fonts.FluentUI.FontFamily. Without this
                    // registration every FontImageSource in AppStyles.xaml renders
                    // as blank tofu, since the .ttf ships but was never aliased.
                    fonts.AddFont("FluentSystemIcons-Regular.ttf", "FluentUI");
                });

            builder.Services.AddSingleton<UserSession>();
            builder.Services.AddSingleton<UserSession>();

            builder.Services.AddHttpClient<FiloApiService>(client =>
            {
                client.BaseAddress = new Uri("https://localhost:7038/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            builder.Services.AddHttpClient("FiloAuth", client =>
            {
                client.BaseAddress = new Uri("https://localhost:7038/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            builder.Services.AddSingleton<AuthService>(services =>
            {
                var factory = services.GetRequiredService<IHttpClientFactory>();
                return new AuthService(factory.CreateClient("FiloAuth"));
            });

            builder.Services.AddTransient<ShowPlaylistsPage>();
            builder.Services.AddTransient<ShowPlaylistSongs>();
            builder.Services.AddTransient<PlaybackPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
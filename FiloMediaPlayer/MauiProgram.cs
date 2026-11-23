using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using FiloMediaPlayer.Services;
using FiloMediaPlayer.Pages;

namespace FiloMediaPlayer
{
    public static class MauiProgram
    {
        // Set at build time with -p:FiloApiBaseUrl=... (see FiloMediaPlayer.csproj).
        public static readonly Uri ApiBaseAddress = GetApiBaseAddress();

        private static Uri GetApiBaseAddress()
        {
            string? configured = typeof(MauiProgram).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "FiloApiBaseUrl")?.Value;

            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            {
                // Tokens and passwords must never travel over plain HTTP.
                throw new InvalidOperationException(
                    $"FiloApiBaseUrl '{configured}' must be an absolute https:// address.");
            }

            // HttpClient needs the trailing slash to keep any path prefix.
            return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
        }

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
            builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();

            builder.Services.AddHttpClient<FiloApiService>(client =>
            {
                client.BaseAddress = ApiBaseAddress;
                // FiloApiService applies its own per-request timeouts so
                // uploads can run longer than ordinary requests.
                client.Timeout = Timeout.InfiniteTimeSpan;
            });

            builder.Services.AddHttpClient<RegisterPage>(client =>
            {
                client.BaseAddress = ApiBaseAddress;
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            builder.Services.AddHttpClient("FiloAuth", client =>
            {
                client.BaseAddress = ApiBaseAddress;
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            builder.Services.AddSingleton<AuthService>(services =>
            {
                var factory = services.GetRequiredService<IHttpClientFactory>();
                return new AuthService(
                    factory.CreateClient("FiloAuth"),
                    services.GetRequiredService<ITokenStore>());
            });

            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<ShowPlaylistsPage>();
            builder.Services.AddTransient<ShowPlaylistSongs>();
            builder.Services.AddTransient<PlaybackPage>();
            builder.Services.AddTransient<CreatePlaylistPage>();
            builder.Services.AddTransient<UploadMediaPage>();
            builder.Services.AddTransient<UploadThumbnailPage>();
            builder.Services.AddTransient<EditPlaylistPage>();
            builder.Services.AddTransient<EditMediaPage>();
            builder.Services.AddTransient<ManagePlaylistMediaPage>();
            builder.Services.AddTransient<ManageMediaPlaylistsPage>();
            builder.Services.AddTransient<MediaGraftingPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
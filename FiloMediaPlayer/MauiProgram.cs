using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

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

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
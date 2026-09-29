using FiloMediaPlayer.Models;
using FiloMediaPlayer.PageModels;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;

            Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object? sender, EventArgs e)
        {
            try
            {
                using var client = new HttpClient();

                var result = await client.GetFromJsonAsync<MauiTestResponse>(
                    "https://localhost:7038/api/MauiTest");

                await DisplayAlert(
                    "API Connection",
                    result?.Message ?? "No response received",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert(
                    "Connection Failed",
                    ex.Message,
                    "OK");
            }
        }
    }

    public class MauiTestResponse
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
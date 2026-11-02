

using FiloMediaPlayer.DTOs;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class LoginPage : ContentPage
{
    private readonly HttpClient _httpClient;
    private readonly UserSession _userSession;
    public LoginPage(UserSession userSession)
	{
		InitializeComponent();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://localhost:7038/");
        _userSession = userSession;
	}


    private async void OnLoginButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button loginButton)
            loginButton.IsEnabled = false;

        try
        {
            string username = UsernameEntry.Text ?? "";
            string password = PasswordEntry.Text ?? "";

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                await DisplayAlertAsync(
                    "Error",
                    "Please enter both username and password.",
                    "OK");
                return;
            }

            LoginInformation info = new LoginInformation(username, password);

            using var response = await _httpClient.PostAsJsonAsync(
                "api/auth/login", info);

            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Login Failed",
                    "Invalid credentials or unable to log in.",
                    "OK");
                return;
            }

            var loginResponse =
                await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (loginResponse == null)
            {
                await DisplayAlertAsync(
                    "Login Error",
                    "The server returned an invalid response.",
                    "OK");
                return;
            }

            await SecureStorage.Default.SetAsync(
                "accessToken", loginResponse.AccessToken);

            await SecureStorage.Default.SetAsync(
                "refreshToken", loginResponse.RefreshToken);

            using var meRequest = new HttpRequestMessage(
                HttpMethod.Get, "api/auth/me");

            meRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", loginResponse.AccessToken);

            using var getResponse = await _httpClient.SendAsync(meRequest);

            if (!getResponse.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Login Error",
                    "Unable to verify your account.",
                    "OK");
                return;
            }

            var currentUser =
                await getResponse.Content
                    .ReadFromJsonAsync<CurrentUserResponse>();

            if (currentUser == null)
            {
                await DisplayAlertAsync(
                    "Login Error",
                    "Could not load user information.",
                    "OK");
                return;
            }

            _userSession.UserId = currentUser.UserId;
            _userSession.Username = currentUser.Username;
            _userSession.Email = currentUser.Email;

            await Shell.Current.GoToAsync("HomePage");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Connection Error",
                "Unable to connect to Filo's server. Check your connection and try again.",
                "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync(
                "Connection Timeout",
                "The server took too long to respond. Please try again.",
                "OK");
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                "Login Error",
                "An unexpected error occurred. Please try again.",
                "OK");
        }
        finally
        {
            if (sender is Button loginsButton)
                loginsButton.IsEnabled = true;
        }
    }


}


using FiloMediaPlayer.DTOs;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Pages;

public partial class LoginPage : ContentPage
{
    private readonly UserSession _userSession;
    private readonly AuthService _authService;
    private readonly FiloApiService _apiService;
    private bool _isLoggingIn;

    public LoginPage(
        UserSession userSession,
        AuthService authService,
        FiloApiService apiService)
	{
		InitializeComponent();
        _userSession = userSession;
        _authService = authService;
        _apiService = apiService;
	}


    private async void OnLoginButtonClicked(object sender, EventArgs e)
    {
        if (_isLoggingIn)
            return;

        _isLoggingIn = true;

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

            var status = await _authService.LoginAsync(username, password);

            switch (status)
            {
                case LoginStatus.InvalidCredentials:
                    await DisplayAlertAsync(
                        "Login Failed",
                        "Incorrect username or password.",
                        "OK");
                    return;

                case LoginStatus.ServerError:
                    await DisplayAlertAsync(
                        "Server Error",
                        "Filo's server couldn't log you in right now. Please try again in a moment.",
                        "OK");
                    return;

                case LoginStatus.InvalidResponse:
                    await DisplayAlertAsync(
                        "Login Error",
                        "The server returned an invalid response.",
                        "OK");
                    return;
            }

            using var getResponse =
                await _apiService.GetWithRetryAsync("api/auth/me");

            if (!getResponse.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Login Error",
                    "Unable to verify your account. Please try again.",
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
        catch (UnauthorizedAccessException)
        {
            await DisplayAlertAsync(
                "Login Failed",
                "Your login could not be verified. Please try again.",
                "OK");
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
            _isLoggingIn = false;

            if (sender is Button loginsButton)
                loginsButton.IsEnabled = true;
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("RegisterPage");
    }

}



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
        string username = UsernameEntry.Text;
        string password = PasswordEntry.Text;

        if(string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlertAsync("Error", "Please enter both username and password.", "OK");
            return;
        }

        LoginInformation info = new LoginInformation(username, password);
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", info);

        if(response.IsSuccessStatusCode)
        {
            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if(loginResponse == null)
            {
                await DisplayAlertAsync("Login", "Something Went Wrong", "Close");
                return;
            }

            await SecureStorage.Default.SetAsync("accessToken", loginResponse.AccessToken);
            await SecureStorage.Default.SetAsync("refreshToken", loginResponse.RefreshToken);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResponse.AccessToken);
            var getResponse = await _httpClient.GetAsync("api/auth/me");
            if(getResponse.IsSuccessStatusCode) 
            {
                var currentUser = await getResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
                if(currentUser == null)
                {
                    await DisplayAlertAsync("Error", "Could not load user information.", "Close");
                    return;
                }

                _userSession.UserId = currentUser.UserId;
                _userSession.Username = currentUser.Username;
                _userSession.Email = currentUser.Email;
                //await DisplayAlertAsync("Success", _userSession.Username, "OK");
                await Shell.Current.GoToAsync("HomePage");
            }
            else 
            {
                await DisplayAlertAsync("Status", "Couldnt verify status code", "Close");
            
            }

        } else
        {
            await DisplayAlertAsync("Login", "Login Failed", "Close");
        }

        //await DisplayAlertAsync("Test", "Login button clicked", "OK");
    }

}
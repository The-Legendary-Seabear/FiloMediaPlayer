using FiloMediaPlayer.DTOs;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text.Json;

namespace FiloMediaPlayer.Pages;


public partial class RegisterPage : ContentPage
{
    private readonly HttpClient _httpClient;
    public RegisterPage(HttpClient httpClient)
	{
		InitializeComponent();
        _httpClient = httpClient;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        if (!RegisterButton.IsEnabled)
            return;
        RegisterButton.IsEnabled = false;
        try
        {
            string username = UsernameEntry.Text?.Trim() ?? "";
            string email = EmailEntry.Text?.Trim() ?? "";
            string password = PasswordEntry.Text ?? "";
            string confirmPassword = ConfirmPasswordEntry.Text ?? "";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                await DisplayAlertAsync(
                    "Missing Information",
                    "Please complete all fields.",
                    "OK");
                return;
            }

            if (password != confirmPassword)
            {
                await DisplayAlertAsync(
                    "Password Mismatch",
                    "Please make sure the passwords match",
                    "OK");
                return;
            }

            if (!MailAddress.TryCreate(email, out var parsedEmail) || parsedEmail.Address != email)
            {
                await DisplayAlertAsync(
                    "Invalid Email",
                    "Please enter a valid email address.",
                    "OK");
                return;
            }

            var registration = new RegisterRequest
            {
                Username = username,
                Email = email,
                Password = password
            };

            using var response = await _httpClient.PostAsJsonAsync("api/auth/register", registration);

            if (response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync(
                    "Registration Successful",
                    "Your account has been created. You can now log in.",
                    "OK");

                await Shell.Current.GoToAsync("..");
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                string errorMessage = "Registration failed. Please check your information.";

                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Identity errors
                    var errors = document.RootElement.Deserialize<List<IdentityErrorResponse>>(new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (errors != null && errors.Count > 0)
                    {
                        errorMessage = string.Join("\n", errors.Select(error => error.Description));
                    }


                }
                else if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Model-validation errors
                    var validation = document.RootElement.Deserialize<ValidationErrorResponse>(new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (validation?.Errors != null && validation.Errors.Count > 0)
                    {
                        errorMessage = string.Join("\n", validation.Errors.Values.SelectMany(message => message));
                    }
                }

                await DisplayAlertAsync(
           "Registration Failed",
           errorMessage,
           "OK");

                return;
            }
            await DisplayAlertAsync(
    "Registration Error",
    "The server could not complete your registration request. Please try again later.",
    "OK");
        }

        catch (HttpRequestException)
        {
            await DisplayAlertAsync(
                "Connection Error",
                "Unable to contact the server. Your account may have been created, so try signing in before registering again.",
                "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync(
                "Connection Timeout",
                "The request timed out. Try signing in before submitting another registration.",
                "OK");
        }
        catch (JsonException)
        {
            await DisplayAlertAsync(
                "Registration Error",
                "The server returned an invalid response. Please try signing in before registering again.",
                "OK");
        }
        finally
        {
            RegisterButton.IsEnabled = true;
        }
    }

    private async void OnBackToLoginClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
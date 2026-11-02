using System;
using System.Collections.Generic;
using System.Text;

using FiloMediaPlayer.DTOs;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Services
{
    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public AuthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }


        public async Task<string?> RefreshAccessTokenAsync(string failedAccessToken)
        {
            await _refreshLock.WaitAsync();

            try
            {
                // Check whether another request already refreshed the token.
                var currentAccessToken =
                    await SecureStorage.Default.GetAsync("accessToken");

                if (!string.IsNullOrEmpty(currentAccessToken) &&
                    currentAccessToken != failedAccessToken)
                {
                    return currentAccessToken;
                }

                // Get the refresh token saved during login.
                var refreshToken =
                    await SecureStorage.Default.GetAsync("refreshToken");

                if (string.IsNullOrEmpty(refreshToken))
                {
                    return null;
                }

                var request = new RefreshRequest
                {
                    RefreshToken = refreshToken
                };

                using var response = await _httpClient.PostAsJsonAsync(
                    "api/auth/refresh", request);

                // The refresh token was rejected.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return null;
                }

                // Other HTTP errors should be handled separately.
                response.EnsureSuccessStatusCode();

                var tokens =
                    await response.Content.ReadFromJsonAsync<LoginResponse>();

                if (tokens == null ||
                    string.IsNullOrEmpty(tokens.AccessToken) ||
                    string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    throw new InvalidOperationException(
                        "The API returned an invalid token response.");
                }

                // Save the replacement tokens.
                await SecureStorage.Default.SetAsync(
                    "refreshToken", tokens.RefreshToken);

                await SecureStorage.Default.SetAsync(
                    "accessToken", tokens.AccessToken);

                return tokens.AccessToken;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

    }
}

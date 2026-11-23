using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

using FiloMediaPlayer.DTOs;
using System.Net.Http.Json;

namespace FiloMediaPlayer.Services
{
    public enum LoginStatus
    {
        Success,
        InvalidCredentials,
        ServerError,
        InvalidResponse
    }

    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenStore _tokenStore;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public AuthService(HttpClient httpClient, ITokenStore tokenStore)
        {
            _httpClient = httpClient;
            _tokenStore = tokenStore;
        }

        /// <summary>
        /// Logs in and stores the tokens. Connection failures throw
        /// (HttpRequestException / TaskCanceledException).
        /// </summary>
        public async Task<LoginStatus> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/auth/login",
                new LoginInformation(username, password),
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.BadRequest)
            {
                return LoginStatus.InvalidCredentials;
            }

            if (!response.IsSuccessStatusCode)
                return LoginStatus.ServerError;

            var tokens = await response.Content
                .ReadFromJsonAsync<LoginResponse>(cancellationToken);

            if (tokens == null ||
                string.IsNullOrEmpty(tokens.AccessToken) ||
                string.IsNullOrEmpty(tokens.RefreshToken))
            {
                return LoginStatus.InvalidResponse;
            }

            await _tokenStore.SetTokensAsync(
                tokens.AccessToken, tokens.RefreshToken);

            return LoginStatus.Success;
        }

        public Task SignOutAsync() => _tokenStore.ClearAsync();

        /// <summary>
        /// Gets a new access token after <paramref name="failedAccessToken"/>
        /// was rejected.
        /// </summary>
        /// <returns>
        /// The new access token, or null when the user must log in again.
        /// Server and connection failures throw instead, so a temporary
        /// outage never signs the user out.
        /// </returns>
        public async Task<string?> RefreshAccessTokenAsync(
            string failedAccessToken,
            CancellationToken cancellationToken = default)
        {
            await _refreshLock.WaitAsync(cancellationToken);

            try
            {
                // Check whether another request already refreshed the token.
                var currentAccessToken = await _tokenStore.GetAccessTokenAsync();

                if (!string.IsNullOrEmpty(currentAccessToken) &&
                    currentAccessToken != failedAccessToken)
                {
                    return currentAccessToken;
                }

                // Get the refresh token saved during login.
                var refreshToken = await _tokenStore.GetRefreshTokenAsync();

                if (string.IsNullOrEmpty(refreshToken))
                {
                    return null;
                }

                var request = new RefreshRequest
                {
                    RefreshToken = refreshToken
                };

                using var response = await _httpClient.PostAsJsonAsync(
                    "api/auth/refresh", request, cancellationToken);

                // The refresh token is invalid or expired.
                if (response.StatusCode is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden
                    or HttpStatusCode.BadRequest)
                {
                    await _tokenStore.ClearAsync();
                    return null;
                }

                // Server problem: keep the tokens so a later retry can work.
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        "The server could not refresh the session.",
                        null,
                        response.StatusCode);
                }

                var tokens = await response.Content
                    .ReadFromJsonAsync<LoginResponse>(cancellationToken);

                if (tokens == null ||
                    string.IsNullOrEmpty(tokens.AccessToken) ||
                    string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    throw new HttpRequestException(
                        "The API returned an invalid token response.");
                }

                // Save the replacement tokens.
                await _tokenStore.SetTokensAsync(
                    tokens.AccessToken, tokens.RefreshToken);

                return tokens.AccessToken;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

    }
}

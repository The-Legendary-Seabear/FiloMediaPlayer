using System;
using System.Collections.Generic;
using System.Text;

using System.Net.Http;

namespace FiloMediaPlayer.Services
{
    public class FiloApiService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;

        public FiloApiService(HttpClient httpClient, AuthService authService)
        {
            _httpClient = httpClient;
            _authService = authService;
        }

        private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(HttpMethod method, string endpoint)
        {
            var token = await SecureStorage.Default.GetAsync("accessToken");

            var request = new HttpRequestMessage(method, endpoint);

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", token);
            }

            return request;
        }



        public async Task<HttpResponseMessage> GetWithRetryAsync(
            string endpoint,
            CancellationToken cancellationToken = default)
        {
            const int maxAttempts = 3;
            var delays = new[] { 500, 1000 };

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var request = await CreateAuthenticatedRequestAsync(
                        HttpMethod.Get, endpoint);

                    var failedToken = request.Headers.Authorization?.Parameter;

                    var response = await _httpClient.SendAsync(
                        request, cancellationToken);

                    // Expired access token: refresh and retry once.
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        response.Dispose();

                        var newToken = await _authService.RefreshAccessTokenAsync(
                            failedToken ?? "");

                        if (string.IsNullOrEmpty(newToken))
                        {
                            throw new UnauthorizedAccessException(
                                "Your session has expired. Please log in again.");
                        }

                        using var retryRequest = new HttpRequestMessage(
                            HttpMethod.Get, endpoint);

                        retryRequest.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue(
                                "Bearer", newToken);

                        return await _httpClient.SendAsync(
                            retryRequest, cancellationToken);
                    }

                    if (response.IsSuccessStatusCode)
                        return response;

                    bool retryable = response.StatusCode is
                        System.Net.HttpStatusCode.RequestTimeout or
                        System.Net.HttpStatusCode.TooManyRequests or
                        System.Net.HttpStatusCode.BadGateway or
                        System.Net.HttpStatusCode.ServiceUnavailable or
                        System.Net.HttpStatusCode.GatewayTimeout;

                    if (!retryable || attempt == maxAttempts - 1)
                        return response;

                    response.Dispose();
                }
                catch (HttpRequestException) when (attempt < maxAttempts - 1)
                {
                    // Temporary network failure.
                }
                catch (TaskCanceledException)
                    when (!cancellationToken.IsCancellationRequested &&
                          attempt < maxAttempts - 1)
                {
                    // Request timed out.
                }

                await Task.Delay(delays[attempt], cancellationToken);
            }

            throw new InvalidOperationException("Unable to complete API request.");
        }


    }
}

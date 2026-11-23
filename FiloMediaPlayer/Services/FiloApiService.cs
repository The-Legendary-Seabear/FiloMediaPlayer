using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;

using System.Net.Http;

namespace FiloMediaPlayer.Services
{
    /// <summary>
    /// Sends authenticated requests to the Filo API.
    /// </summary>
    /// <remarks>
    /// - An expired access token is refreshed once and the request is sent
    ///   again. If the session cannot be renewed a
    ///   <see cref="SessionExpiredException"/> is thrown.
    /// - Only GET requests are retried on connection failures and
    ///   temporary server errors, so a write is never sent twice.
    /// - Timeouts throw <see cref="TaskCanceledException"/> with a
    ///   <see cref="TimeoutException"/> inner exception.
    /// </remarks>
    public class FiloApiService
    {
        // Settable for tests.
        internal static TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        internal static TimeSpan UploadTimeout = TimeSpan.FromMinutes(30);

        // Delays between GET attempts (three attempts in total).
        internal static TimeSpan[] RetryDelays =
        {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000)
        };

        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private readonly ITokenStore _tokenStore;

        public FiloApiService(
            HttpClient httpClient,
            AuthService authService,
            ITokenStore tokenStore)
        {
            _httpClient = httpClient;
            _authService = authService;
            _tokenStore = tokenStore;
        }

        public Task<HttpResponseMessage> GetWithRetryAsync(
            string endpoint,
            CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Get, endpoint, null, retry: true,
                DefaultTimeout, cancellationToken);

        public Task<HttpResponseMessage> PostAsync<T>(
            string endpoint,
            T data,
            CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Post, endpoint, JsonBody(data), retry: false,
                DefaultTimeout, cancellationToken);

        public Task<HttpResponseMessage> PostEmptyAsync(
            string endpoint,
            CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Post, endpoint, null, retry: false,
                DefaultTimeout, cancellationToken);

        public Task<HttpResponseMessage> PutAsync<T>(
            string endpoint,
            T data,
            CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Put, endpoint, JsonBody(data), retry: false,
                DefaultTimeout, cancellationToken);

        public Task<HttpResponseMessage> DeleteAsync(
            string endpoint,
            CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Delete, endpoint, null, retry: false,
                DefaultTimeout, cancellationToken);

        /// <summary>
        /// Uploads multipart content. <paramref name="createContent"/> is
        /// called for each send (the upload is repeated once if the access
        /// token had to be refreshed), so it must open a fresh stream.
        /// </summary>
        public Task<HttpResponseMessage> PostMultipartAsync(
            string endpoint,
            Func<Task<HttpContent>> createContent,
            CancellationToken cancellationToken = default)
            => SendAsync(HttpMethod.Post, endpoint, createContent, retry: false,
                UploadTimeout, cancellationToken);

        private static Func<Task<HttpContent>> JsonBody<T>(T data)
            => () => Task.FromResult<HttpContent>(JsonContent.Create(data));

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string endpoint,
            Func<Task<HttpContent>>? createContent,
            bool retry,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            int maxAttempts = retry ? RetryDelays.Length + 1 : 1;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    var response = await SendAuthorizedAsync(
                        method, endpoint, createContent, timeout, cancellationToken);

                    if (attempt >= maxAttempts || !IsTransient(response.StatusCode))
                        return response;

                    response.Dispose();
                }
                catch (HttpRequestException) when (attempt < maxAttempts)
                {
                    // Connection failure: try again after a short delay.
                    // Timeouts are not retried; the user already waited.
                }

                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }
        }

        private async Task<HttpResponseMessage> SendAuthorizedAsync(
            HttpMethod method,
            string endpoint,
            Func<Task<HttpContent>>? createContent,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var token = await _tokenStore.GetAccessTokenAsync();

            var response = await SendOnceAsync(
                method, endpoint, createContent, token, timeout, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            // Expired access token: refresh and retry once.
            response.Dispose();

            var newToken = await _authService.RefreshAccessTokenAsync(
                token ?? "", cancellationToken);

            if (string.IsNullOrEmpty(newToken))
                throw new SessionExpiredException();

            response = await SendOnceAsync(
                method, endpoint, createContent, newToken, timeout, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new SessionExpiredException();
            }

            return response;
        }

        private async Task<HttpResponseMessage> SendOnceAsync(
            HttpMethod method,
            string endpoint,
            Func<Task<HttpContent>>? createContent,
            string? token,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, endpoint);

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            if (createContent != null)
                request.Content = await createContent();

            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeoutCts.CancelAfter(timeout);

            try
            {
                return await _httpClient.SendAsync(request, timeoutCts.Token);
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException(
                    "The server took too long to respond.",
                    new TimeoutException(ex.Message, ex));
            }
        }

        private static bool IsTransient(HttpStatusCode statusCode) =>
            statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
    }
}

using System.Net;

namespace FiloMediaPlayer.Services
{
    /// <summary>
    /// Shows API failures to the user in a consistent way.
    /// </summary>
    public static class ErrorPresenter
    {
        private static int _redirectingToLogin;

        /// <summary>
        /// Shows why an operation failed. On an expired session the user is
        /// sent back to the login page.
        /// </summary>
        public static async Task ShowAsync(
            Page page,
            Exception ex,
            string action,
            bool mayHaveSucceeded = false)
        {
            System.Diagnostics.Debug.WriteLine($"API error ({action}): {ex}");

            await AlertAsync(page, GetTitle(ex), Describe(ex, action, mayHaveSucceeded));

            if (ex is UnauthorizedAccessException)
                await GoToLoginAsync();
        }

        public static async Task ShowAsync(
            Page page,
            HttpResponseMessage response,
            string action)
        {
            await AlertAsync(
                page,
                ApiErrors.GetTitle(response.StatusCode),
                await ApiErrors.DescribeAsync(response, action));
        }

        /// <summary>
        /// Shows a failed load and offers to try again.
        /// </summary>
        /// <returns>True if the user chose Retry.</returns>
        public static async Task<bool> ShowLoadFailureAsync(
            Page page,
            Exception ex,
            string action)
        {
            System.Diagnostics.Debug.WriteLine($"API error ({action}): {ex}");

            if (ex is UnauthorizedAccessException || !ApiErrors.IsConnectionFailure(ex))
            {
                await ShowAsync(page, ex, action);
                return false;
            }

            return await ConfirmAsync(
                page, GetTitle(ex), Describe(ex, action, false), "Retry", "Cancel");
        }

        public static async Task<bool> ShowLoadFailureAsync(
            Page page,
            HttpResponseMessage response,
            string action)
        {
            string title = ApiErrors.GetTitle(response.StatusCode);
            string message = await ApiErrors.DescribeAsync(response, action);

            bool canRetry = (int)response.StatusCode >= 500 ||
                response.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests;

            if (!canRetry)
            {
                await AlertAsync(page, title, message);
                return false;
            }

            return await ConfirmAsync(page, title, message, "Retry", "Cancel");
        }

        private static bool IsOffline()
        {
            try
            {
                return Connectivity.Current.NetworkAccess is
                    NetworkAccess.None or NetworkAccess.Local;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetTitle(Exception ex) =>
            ApiErrors.IsConnectionFailure(ex) && IsOffline()
                ? "No Connection"
                : ApiErrors.GetTitle(ex);

        private static string Describe(Exception ex, string action, bool mayHaveSucceeded)
        {
            if (ex is HttpRequestException && IsOffline())
            {
                return $"You appear to be offline, so Filo couldn't {action}. " +
                    "Reconnect and try again." +
                    (mayHaveSucceeded ? ApiErrors.MayHaveSucceededHint : "");
            }

            return ApiErrors.Describe(ex, action, mayHaveSucceeded);
        }

        private static Task AlertAsync(Page page, string title, string message) =>
            MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // The user may have left the page while the request ran.
                if (page.Window != null)
                    await page.DisplayAlertAsync(title, message, "OK");
            });

        private static Task<bool> ConfirmAsync(
            Page page, string title, string message, string accept, string cancel) =>
            MainThread.InvokeOnMainThreadAsync(async () =>
                page.Window != null &&
                await page.DisplayAlertAsync(title, message, accept, cancel));

        private static async Task GoToLoginAsync()
        {
            if (Interlocked.Exchange(ref _redirectingToLogin, 1) == 1)
                return;

            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (Shell.Current != null)
                        await Shell.Current.GoToAsync("//login");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login redirect failed: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _redirectingToLogin, 0);
            }
        }
    }
}

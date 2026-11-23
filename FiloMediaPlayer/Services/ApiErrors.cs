using System.Net;
using System.Text.Json;

namespace FiloMediaPlayer.Services
{
    /// <summary>
    /// Turns failed API calls into short, user-facing messages.
    /// </summary>
    public static class ApiErrors
    {
        public const string MayHaveSucceededHint =
            " The change may still have been saved, so check before trying again.";

        public static bool IsTimeout(Exception ex) =>
            ex is TimeoutException ||
            (ex is TaskCanceledException && ex.InnerException is TimeoutException);

        /// <summary>
        /// Network, DNS, refused-connection and timeout failures.
        /// </summary>
        public static bool IsConnectionFailure(Exception ex) =>
            ex is HttpRequestException || IsTimeout(ex);

        public static string GetTitle(Exception ex) => ex switch
        {
            UnauthorizedAccessException => "Session Expired",
            _ when IsTimeout(ex) => "Request Timed Out",
            HttpRequestException => "Connection Error",
            _ => "Error"
        };

        /// <param name="action">What was being done, e.g. "save the playlist".</param>
        /// <param name="mayHaveSucceeded">
        /// True for writes where the server may have applied the change
        /// before the connection failed.
        /// </param>
        public static string Describe(
            Exception ex,
            string action,
            bool mayHaveSucceeded = false)
        {
            if (ex is UnauthorizedAccessException)
                return "Your session has expired. Please log in again.";

            if (IsTimeout(ex))
            {
                return "The server took too long to respond." +
                    (mayHaveSucceeded ? MayHaveSucceededHint : " Please try again.");
            }

            if (ex is HttpRequestException)
            {
                return $"Unable to reach the Filo server to {action}. " +
                    "Check your connection and try again." +
                    (mayHaveSucceeded ? MayHaveSucceededHint : "");
            }

            return $"Something went wrong while trying to {action}. Please try again.";
        }

        public static string GetTitle(HttpStatusCode statusCode) => statusCode switch
        {
            HttpStatusCode.BadRequest => "Check Your Information",
            HttpStatusCode.Unauthorized => "Session Expired",
            HttpStatusCode.Forbidden => "Not Allowed",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Server Error"
        };

        /// <summary>
        /// Message for a non-success response. Only short validation
        /// messages from the server are shown; anything else is replaced
        /// with a generic message.
        /// </summary>
        public static async Task<string> DescribeAsync(
            HttpResponseMessage response,
            string action)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.BadRequest:
                    return await ReadValidationMessageAsync(response)
                        ?? $"The server could not {action}. Check the information and try again.";

                case HttpStatusCode.Unauthorized:
                    return "Your session has expired. Please log in again.";

                case HttpStatusCode.Forbidden:
                    return $"You don't have permission to {action}.";

                case HttpStatusCode.NotFound:
                    return "The item could not be found. It may have been deleted.";

                case HttpStatusCode.Conflict:
                    return await ReadValidationMessageAsync(response)
                        ?? "This change conflicts with the current data. Refresh and try again.";

                case HttpStatusCode.RequestEntityTooLarge:
                    return "The file is too large to upload.";

                default:
                    return $"The server could not {action} right now. Please try again in a moment.";
            }
        }

        private static async Task<string?> ReadValidationMessageAsync(
            HttpResponseMessage response)
        {
            string body;

            try
            {
                body = (await response.Content.ReadAsStringAsync()).Trim();
            }
            catch (Exception)
            {
                return null;
            }

            if (body.Length == 0)
                return null;

            if (body[0] is '{' or '[')
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    var messages = ExtractMessages(document.RootElement)
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Distinct()
                        .Take(5)
                        .ToList();

                    return messages.Count > 0 ? string.Join("\n", messages) : null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            // Plain-text messages such as BadRequest("...") from the API.
            if (body.Length <= 200 && body[0] != '<')
                return body.Trim('"');

            return null;
        }

        private static IEnumerable<string> ExtractMessages(JsonElement root)
        {
            // ASP.NET Identity errors: [{ "code": ..., "description": ... }]
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object &&
                        TryGetString(item, "description", out var description))
                    {
                        yield return description;
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        yield return item.GetString()!;
                    }
                }

                yield break;
            }

            if (root.ValueKind != JsonValueKind.Object)
                yield break;

            // Model validation: { "errors": { "Field": ["message"] } }
            if (TryGetProperty(root, "errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var message in field.Value.EnumerateArray())
                    {
                        if (message.ValueKind == JsonValueKind.String)
                            yield return message.GetString()!;
                    }
                }

                yield break;
            }

            // { "message": "..." } or ProblemDetails "detail".
            if (TryGetString(root, "message", out var text) ||
                TryGetString(root, "detail", out text))
            {
                yield return text;
            }
        }

        private static bool TryGetString(JsonElement element, string name, out string value)
        {
            value = "";

            if (TryGetProperty(element, name, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString() ?? "";
                return true;
            }

            return false;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}

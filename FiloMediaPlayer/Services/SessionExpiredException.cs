namespace FiloMediaPlayer.Services
{
    /// <summary>
    /// The user must log in again: the refresh token is missing, expired or
    /// was rejected by the server. Not thrown for connection failures.
    /// </summary>
    /// <remarks>
    /// Derives from <see cref="UnauthorizedAccessException"/> so existing
    /// catch blocks keep working.
    /// </remarks>
    public class SessionExpiredException : UnauthorizedAccessException
    {
        public SessionExpiredException()
            : base("Your session has expired. Please log in again.")
        {
        }
    }
}

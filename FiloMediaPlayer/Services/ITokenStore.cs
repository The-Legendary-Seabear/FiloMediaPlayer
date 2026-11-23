namespace FiloMediaPlayer.Services
{
    /// <summary>
    /// Stores the API access and refresh tokens.
    /// </summary>
    public interface ITokenStore
    {
        Task<string?> GetAccessTokenAsync();
        Task<string?> GetRefreshTokenAsync();
        Task SetTokensAsync(string accessToken, string refreshToken);
        Task ClearAsync();
    }
}

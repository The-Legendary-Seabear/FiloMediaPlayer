namespace FiloMediaPlayer.Services
{
    /// <summary>
    /// Keeps the API tokens in the platform's secure storage.
    /// </summary>
    public class SecureTokenStore : ITokenStore
    {
        private const string AccessTokenKey = "accessToken";
        private const string RefreshTokenKey = "refreshToken";

        public Task<string?> GetAccessTokenAsync() =>
            SecureStorage.Default.GetAsync(AccessTokenKey);

        public Task<string?> GetRefreshTokenAsync() =>
            SecureStorage.Default.GetAsync(RefreshTokenKey);

        public async Task SetTokensAsync(string accessToken, string refreshToken)
        {
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
            await SecureStorage.Default.SetAsync(AccessTokenKey, accessToken);
        }

        public Task ClearAsync()
        {
            SecureStorage.Default.Remove(AccessTokenKey);
            SecureStorage.Default.Remove(RefreshTokenKey);
            return Task.CompletedTask;
        }
    }
}

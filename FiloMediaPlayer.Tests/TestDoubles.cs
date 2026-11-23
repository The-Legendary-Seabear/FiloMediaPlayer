using System.Net;
using System.Net.Http.Json;
using FiloMediaPlayer.DTOs;
using FiloMediaPlayer.Services;

namespace FiloMediaPlayer.Tests;

internal sealed class InMemoryTokenStore : ITokenStore
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ClearCount { get; private set; }

    public Task<string?> GetAccessTokenAsync() => Task.FromResult(AccessToken);
    public Task<string?> GetRefreshTokenAsync() => Task.FromResult(RefreshToken);

    public Task SetTokensAsync(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        AccessToken = null;
        RefreshToken = null;
        ClearCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Simulated server. Records every request it receives.
/// </summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _respond;
    private int _count;

    public FakeServer(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public FakeServer(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        : this((request, n, _) => Task.FromResult(respond(request, n)))
    {
    }

    public List<(HttpMethod Method, string Path, string? Token, string? Body)> Requests { get; } = new();

    public int CountFor(string path) => Requests.Count(r => r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Parameter, body));
        }

        int n = Interlocked.Increment(ref _count);
        return await _respond(request, n, cancellationToken);
    }

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    public static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    public static HttpResponseMessage Tokens(string access, string refresh) =>
        Json(new LoginResponse { AccessToken = access, RefreshToken = refresh, TokenType = "Bearer" });
}

internal sealed class ApiHarness
{
    public static readonly Uri BaseAddress = new("https://filo.test/");

    public ApiHarness(FakeServer server, string? accessToken = "old-access", string? refreshToken = "refresh-1")
    {
        Server = server;
        Tokens = new InMemoryTokenStore { AccessToken = accessToken, RefreshToken = refreshToken };

        var client = new HttpClient(server) { BaseAddress = BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        Auth = new AuthService(client, Tokens);
        Api = new FiloApiService(client, Auth, Tokens);
    }

    public FakeServer Server { get; }
    public InMemoryTokenStore Tokens { get; }
    public AuthService Auth { get; }
    public FiloApiService Api { get; }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FiloMediaPlayer.Services;

namespace FiloMediaPlayer.Tests;

// FiloApiService keeps its retry delays and timeouts in static fields, so
// these tests must not run in parallel.
[CollectionDefinition("FiloApiService", DisableParallelization = true)]
public class FiloApiServiceCollection { }

[Collection("FiloApiService")]
public class FiloApiServiceTests : IDisposable
{
    private readonly TimeSpan[] _originalDelays = FiloApiService.RetryDelays;
    private readonly TimeSpan _originalTimeout = FiloApiService.DefaultTimeout;

    public FiloApiServiceTests()
    {
        FiloApiService.RetryDelays = new[] { TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20) };
    }

    public void Dispose()
    {
        FiloApiService.RetryDelays = _originalDelays;
        FiloApiService.DefaultTimeout = _originalTimeout;
    }

    // ---- Transient failures and recovery (scenarios 2, 3) ----

    [Fact]
    public async Task Get_TemporaryServerError_IsRetried_ThenSucceeds()
    {
        var h = new ApiHarness(new FakeServer((_, n) =>
            FakeServer.Status(n == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));

        using var response = await h.Api.GetWithRetryAsync("api/playlists");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Server.Requests.Count);
    }

    [Fact]
    public async Task Get_ConnectionDrop_IsRetried_ThenSucceeds()
    {
        var h = new ApiHarness(new FakeServer((_, n) =>
            n == 1 ? throw new HttpRequestException("connection reset") : FakeServer.Status(HttpStatusCode.OK)));

        using var response = await h.Api.GetWithRetryAsync("api/media");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Server.Requests.Count);
    }

    [Fact]
    public async Task Get_PersistentServerError_StopsAfterThreeAttempts()
    {
        var h = new ApiHarness(new FakeServer((_, _) => FakeServer.Status(HttpStatusCode.BadGateway)));

        using var response = await h.Api.GetWithRetryAsync("api/playlists");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, h.Server.Requests.Count);
    }

    [Fact]
    public async Task Get_PersistentConnectionFailure_ThrowsAfterThreeAttempts()
    {
        var h = new ApiHarness(new FakeServer((_, _) => throw new HttpRequestException("no route")));

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.GetWithRetryAsync("api/playlists"));
        Assert.Equal(3, h.Server.Requests.Count);
    }

    [Fact]
    public async Task Get_NonTransientErrors_AreNotRetried()
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest,
                                       HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
        {
            var h = new ApiHarness(new FakeServer((_, _) => FakeServer.Status(status)));

            using var response = await h.Api.GetWithRetryAsync("api/media/1");

            Assert.Equal(status, response.StatusCode);
            Assert.Single(h.Server.Requests);
        }
    }

    // ---- Writes are never repeated (scenarios 7, 8) ----

    [Fact]
    public async Task Writes_AreNotRetried_OnConnectionFailure()
    {
        var h = new ApiHarness(new FakeServer((_, _) => throw new HttpRequestException("connection reset")));

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.PostAsync("api/playlists/create", new { Name = "x" }));
        await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.PutAsync("api/media/1", new { Title = "x" }));
        await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.DeleteAsync("api/playlists/1/media/2"));
        await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.PostEmptyAsync("api/playlists/1/media/2"));

        Assert.Equal(4, h.Server.Requests.Count);
    }

    [Fact]
    public async Task Writes_AreNotRetried_OnServerUnavailable()
    {
        var h = new ApiHarness(new FakeServer((_, _) => FakeServer.Status(HttpStatusCode.ServiceUnavailable)));

        using var response = await h.Api.PostAsync("api/processing-jobs/grafting", new { Title = "x" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(h.Server.Requests);
    }

    // ---- Authentication (scenarios 5, 6) ----

    [Fact]
    public async Task ExpiredAccessToken_IsRefreshed_AndRequestResentWithNewToken()
    {
        var h = new ApiHarness(new FakeServer((request, _) =>
            request.RequestUri!.AbsolutePath == "/api/auth/refresh"
                ? FakeServer.Tokens("new-access", "refresh-2")
                : request.Headers.Authorization?.Parameter == "new-access"
                    ? FakeServer.Status(HttpStatusCode.OK)
                    : FakeServer.Status(HttpStatusCode.Unauthorized)));

        using var response = await h.Api.GetWithRetryAsync("api/playlists");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, h.Server.CountFor("/api/auth/refresh"));
        Assert.Equal("new-access", h.Server.Requests.Last().Token);
        Assert.Equal("new-access", h.Tokens.AccessToken);
        Assert.Equal("refresh-2", h.Tokens.RefreshToken);
    }

    [Fact]
    public async Task ExpiredAccessToken_OnWrite_RefreshesAndSendsTheBodyAgain()
    {
        var h = new ApiHarness(new FakeServer((request, _) =>
            request.RequestUri!.AbsolutePath == "/api/auth/refresh"
                ? FakeServer.Tokens("new-access", "refresh-2")
                : request.Headers.Authorization?.Parameter == "new-access"
                    ? FakeServer.Status(HttpStatusCode.OK)
                    : FakeServer.Status(HttpStatusCode.Unauthorized)));

        using var response = await h.Api.PutAsync("api/media/7", new { Title = "Song" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var puts = h.Server.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        Assert.Equal(2, puts.Count);
        Assert.All(puts, r => Assert.Contains("Song", r.Body));
    }

    [Fact]
    public async Task ConcurrentExpiredRequests_ShareASingleRefresh()
    {
        var h = new ApiHarness(new FakeServer(async (request, _, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
            {
                await Task.Delay(50, ct);
                return FakeServer.Tokens("new-access", "refresh-2");
            }

            return FakeServer.Status(request.Headers.Authorization?.Parameter == "new-access"
                ? HttpStatusCode.OK
                : HttpStatusCode.Unauthorized);
        }));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(i => h.Api.GetWithRetryAsync($"api/media/{i}")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, h.Server.CountFor("/api/auth/refresh"));
    }

    [Fact]
    public async Task RejectedRefreshToken_SignsOut_WithoutLooping()
    {
        var h = new ApiHarness(new FakeServer((request, _) =>
            FakeServer.Status(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<SessionExpiredException>(() => h.Api.GetWithRetryAsync("api/playlists"));

        Assert.Equal(2, h.Server.Requests.Count); // one API call + one refresh
        Assert.Null(h.Tokens.AccessToken);
        Assert.Null(h.Tokens.RefreshToken);
    }

    [Fact]
    public async Task MissingRefreshToken_ThrowsSessionExpired_WithoutCallingRefresh()
    {
        var h = new ApiHarness(
            new FakeServer((_, _) => FakeServer.Status(HttpStatusCode.Unauthorized)),
            accessToken: "old-access",
            refreshToken: null);

        await Assert.ThrowsAsync<SessionExpiredException>(() => h.Api.DeleteAsync("api/media-bookmarks/3"));

        Assert.Equal(0, h.Server.CountFor("/api/auth/refresh"));
    }

    [Fact]
    public async Task StillUnauthorizedAfterRefresh_StopsInsteadOfLooping()
    {
        var h = new ApiHarness(new FakeServer((request, _) =>
            request.RequestUri!.AbsolutePath == "/api/auth/refresh"
                ? FakeServer.Tokens("new-access", "refresh-2")
                : FakeServer.Status(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<SessionExpiredException>(() => h.Api.GetWithRetryAsync("api/playlists"));

        Assert.Equal(1, h.Server.CountFor("/api/auth/refresh"));
        Assert.Equal(3, h.Server.Requests.Count);
    }

    [Theory]
    [InlineData(true)]   // refresh endpoint unreachable
    [InlineData(false)]  // refresh endpoint returns 500
    public async Task RefreshFailsBecauseServerIsDown_KeepsTokens(bool networkFailure)
    {
        var h = new ApiHarness(new FakeServer((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/auth/refresh")
                return FakeServer.Status(HttpStatusCode.Unauthorized);

            return networkFailure
                ? throw new HttpRequestException("connection refused")
                : FakeServer.Status(HttpStatusCode.InternalServerError);
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.PostAsync("api/playlists/create", new { }));

        Assert.True(ApiErrors.IsConnectionFailure(ex));
        Assert.Equal("old-access", h.Tokens.AccessToken);
        Assert.Equal("refresh-1", h.Tokens.RefreshToken);
        Assert.Equal(0, h.Tokens.ClearCount);
    }

    // ---- Timeouts and cancellation ----

    [Fact]
    public async Task SlowServer_TimesOut_AsTimeout_AndIsNotRetried()
    {
        FiloApiService.DefaultTimeout = TimeSpan.FromMilliseconds(200);

        var h = new ApiHarness(new FakeServer(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeServer.Status(HttpStatusCode.OK);
        }));

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => h.Api.GetWithRetryAsync("api/playlists"));

        Assert.IsType<TimeoutException>(ex.InnerException);
        Assert.True(ApiErrors.IsTimeout(ex));
        Assert.Single(h.Server.Requests);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsTimeout_AndIsNotRetried()
    {
        using var cts = new CancellationTokenSource();

        var h = new ApiHarness(new FakeServer(async (_, _, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeServer.Status(HttpStatusCode.OK);
        }));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Api.GetWithRetryAsync("api/processing-jobs/1", cts.Token));

        Assert.False(ApiErrors.IsTimeout(ex));
        Assert.Single(h.Server.Requests);
    }

    // ---- Uploads (scenario 7) ----

    [Fact]
    public async Task Upload_AfterTokenRefresh_RebuildsTheContent()
    {
        int contentCreated = 0;

        var h = new ApiHarness(new FakeServer((request, _) =>
            request.RequestUri!.AbsolutePath == "/api/auth/refresh"
                ? FakeServer.Tokens("new-access", "refresh-2")
                : FakeServer.Status(request.Headers.Authorization?.Parameter == "new-access"
                    ? HttpStatusCode.OK
                    : HttpStatusCode.Unauthorized)));

        using var response = await h.Api.PostMultipartAsync("api/media/upload", () =>
        {
            contentCreated++;
            var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "song.mp3" },
                { new StringContent("Song"), "name" }
            };
            return Task.FromResult<HttpContent>(form);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, contentCreated);
    }

    [Fact]
    public async Task Upload_FailedConnection_IsNotRetried()
    {
        var h = new ApiHarness(new FakeServer((_, _) => throw new HttpRequestException("connection reset")));

        await Assert.ThrowsAsync<HttpRequestException>(() => h.Api.PostMultipartAsync(
            "api/media/upload",
            () => Task.FromResult<HttpContent>(new StringContent("x"))));

        Assert.Single(h.Server.Requests);
    }

    // ---- Real sockets: API down / DNS failure (scenarios 1, 4) ----

    [Fact]
    public async Task ApiNotRunning_FailsFast_AsConnectionError()
    {
        int port = GetUnusedPort();
        var tokens = new InMemoryTokenStore { AccessToken = "a", RefreshToken = "r" };
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
        var api = new FiloApiService(client, new AuthService(client, tokens), tokens);

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetWithRetryAsync("api/playlists"));

        Assert.True(ApiErrors.IsConnectionFailure(ex));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
        Assert.Equal("a", tokens.AccessToken); // not signed out
    }

    [Fact]
    public async Task UnknownHost_FailsAsConnectionError()
    {
        var tokens = new InMemoryTokenStore();
        var client = new HttpClient { BaseAddress = new Uri("http://filo-api.invalid/"), Timeout = Timeout.InfiniteTimeSpan };
        var api = new FiloApiService(client, new AuthService(client, tokens), tokens);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetWithRetryAsync("api/playlists"));

        Assert.True(ApiErrors.IsConnectionFailure(ex));
    }

    [Fact]
    public async Task Login_ApiNotRunning_ThrowsConnectionError_AndStoresNothing()
    {
        int port = GetUnusedPort();
        var tokens = new InMemoryTokenStore();
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(15) };
        var auth = new AuthService(client, tokens);

        await Assert.ThrowsAsync<HttpRequestException>(() => auth.LoginAsync("user", "password"));

        Assert.Null(tokens.AccessToken);
    }

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

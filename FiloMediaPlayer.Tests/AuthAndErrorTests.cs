using System.Net;
using System.Text;
using FiloMediaPlayer.Services;

namespace FiloMediaPlayer.Tests;

public class AuthServiceLoginTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LoginStatus.InvalidCredentials)]
    [InlineData(HttpStatusCode.BadRequest, LoginStatus.InvalidCredentials)]
    [InlineData(HttpStatusCode.InternalServerError, LoginStatus.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, LoginStatus.ServerError)]
    public async Task Login_DistinguishesBadCredentialsFromServerProblems(
        HttpStatusCode status, LoginStatus expected)
    {
        var h = new ApiHarness(new FakeServer((_, _) => FakeServer.Status(status)), null, null);

        Assert.Equal(expected, await h.Auth.LoginAsync("user", "password"));
        Assert.Null(h.Tokens.AccessToken);
    }

    [Fact]
    public async Task Login_Success_StoresTokens()
    {
        var h = new ApiHarness(new FakeServer((_, _) => FakeServer.Tokens("access", "refresh")), null, null);

        Assert.Equal(LoginStatus.Success, await h.Auth.LoginAsync("user", "password"));
        Assert.Equal("access", h.Tokens.AccessToken);
        Assert.Equal("refresh", h.Tokens.RefreshToken);
    }
}

public class ApiErrorsTests
{
    [Fact]
    public void ConnectionFailures_AreRecognised()
    {
        Assert.True(ApiErrors.IsConnectionFailure(new HttpRequestException()));
        Assert.True(ApiErrors.IsConnectionFailure(new TaskCanceledException("t", new TimeoutException())));
        Assert.False(ApiErrors.IsConnectionFailure(new TaskCanceledException()));
        Assert.False(ApiErrors.IsConnectionFailure(new InvalidOperationException()));
        Assert.False(ApiErrors.IsConnectionFailure(new SessionExpiredException()));
    }

    [Fact]
    public void Messages_AreFriendly_AndWarnWhenAWriteMayHaveSucceeded()
    {
        var timeout = new TaskCanceledException("t", new TimeoutException());

        Assert.Equal("Request Timed Out", ApiErrors.GetTitle(timeout));
        Assert.Contains("Please try again", ApiErrors.Describe(timeout, "load your playlists"));
        Assert.Contains("may still have been saved", ApiErrors.Describe(timeout, "create the playlist", mayHaveSucceeded: true));

        var network = new HttpRequestException("No such host is known. (filo-api.invalid:443)");
        string message = ApiErrors.Describe(network, "load your playlists");
        Assert.Equal("Connection Error", ApiErrors.GetTitle(network));
        Assert.Contains("load your playlists", message);
        Assert.DoesNotContain("filo-api.invalid", message); // no technical details

        Assert.Equal("Session Expired", ApiErrors.GetTitle(new SessionExpiredException()));
        Assert.Contains("log in again", ApiErrors.Describe(new SessionExpiredException(), "x"));
    }

    [Fact]
    public async Task ValidationErrors_ShowTheServersMessages()
    {
        var modelState = Response(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"Name":["The Name field is required."]}}""");
        Assert.Equal("The Name field is required.", await ApiErrors.DescribeAsync(modelState, "save"));

        var identity = Response(HttpStatusCode.BadRequest,
            """[{"code":"DuplicateUserName","description":"Username 'bob' is already taken."}]""");
        Assert.Equal("Username 'bob' is already taken.", await ApiErrors.DescribeAsync(identity, "register"));

        var plain = Response(HttpStatusCode.BadRequest, "Invalid or unsafe loudness values.");
        Assert.Equal("Invalid or unsafe loudness values.", await ApiErrors.DescribeAsync(plain, "save"));
    }

    [Fact]
    public async Task ServerErrors_DoNotLeakResponseBodies()
    {
        var crash = Response(HttpStatusCode.InternalServerError,
            "System.NullReferenceException: at Filo.Api.Controllers.MediaController ... ConnectionString=Host=db;Password=secret");

        string message = await ApiErrors.DescribeAsync(crash, "update the media");

        Assert.DoesNotContain("Exception", message);
        Assert.DoesNotContain("secret", message);
        Assert.Contains("update the media", message);

        var html = Response(HttpStatusCode.BadRequest, "<html><body>Bad Request</body></html>");
        Assert.DoesNotContain("<html>", await ApiErrors.DescribeAsync(html, "save"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "could not be found")]
    [InlineData(HttpStatusCode.Forbidden, "permission")]
    [InlineData(HttpStatusCode.Conflict, "conflicts")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "try again")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "too large")]
    public async Task StatusCodes_HaveSpecificMessages(HttpStatusCode status, string expected)
    {
        Assert.Contains(expected, await ApiErrors.DescribeAsync(Response(status, ""), "save the playlist"));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8) };
}

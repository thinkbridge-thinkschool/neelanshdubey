using System.Net;
using System.Net.Http.Json;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// Exercises the real InternalJwt login/refresh/logout pipeline end to end -
// real password hashing, real JWT issuance, real EF-backed refresh-token
// storage - swapping out only IClock so refresh-token expiry can be proven
// deterministically instead of by waiting on real time.
public class AuthEndpointsTests : IntegrationTestBase
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessAndRefreshTokens()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = UserSeed.Email, Password = UserSeed.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = UserSeed.Email, Password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokenPair()
    {
        var tokens = await LoginAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var newTokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotEqual(tokens.RefreshToken, newTokens!.RefreshToken);
    }

    [Fact]
    public async Task Refresh_AfterFakeClockAdvancesPastExpiry_ReturnsUnauthorized()
    {
        var tokens = await LoginAsync();

        // The refresh token is valid for 7 days from the moment it was
        // issued (measured against the fake clock). Jumping the clock 8
        // days forward proves the expiry check without any real waiting.
        Clock.UtcNow = Clock.UtcNow.AddDays(8);

        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithFakeClockAdvancedButStillWithinValidity_Succeeds()
    {
        var tokens = await LoginAsync();

        // 6 days is still inside the 7-day refresh-token validity window.
        Clock.UtcNow = Clock.UtcNow.AddDays(6);

        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ChainedTwice_EachNewTokenWorksUntilOldOneIsReused()
    {
        var loginTokens = await LoginAsync();
        var tokenA = loginTokens.RefreshToken;

        var firstRefreshResponse = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokenA });
        Assert.Equal(HttpStatusCode.OK, firstRefreshResponse.StatusCode);
        var tokenB = (await firstRefreshResponse.Content.ReadFromJsonAsync<TokenResponse>())!.RefreshToken;

        var secondRefreshResponse = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokenB });
        Assert.Equal(HttpStatusCode.OK, secondRefreshResponse.StatusCode);
        var tokenC = (await secondRefreshResponse.Content.ReadFromJsonAsync<TokenResponse>())!.RefreshToken;

        var reuseOfARespose = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokenA });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseOfARespose.StatusCode);

        var attemptWithCAfterReuse = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokenC });
        Assert.Equal(HttpStatusCode.Unauthorized, attemptWithCAfterReuse.StatusCode);
    }

    [Fact]
    public async Task Logout_WithValidToken_RevokesRefreshTokenAndBlocksFurtherRefresh()
    {
        var tokens = await LoginAsync();

        var logoutResponse = await Client.PostAsJsonAsync(
            "/api/auth/logout",
            new LogoutRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }
}

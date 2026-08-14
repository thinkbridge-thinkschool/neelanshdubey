using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Tests;

// Exercises the real InternalJwt authentication + authorization + refresh-token
// pipeline end to end (no TestAuthHandler substitution), covering the matrix:
// anonymous -> 401, wrong policy -> 403, correct policy -> 200,
// expired access token -> 401, revoked/reused refresh token -> 401.
public class AuthenticationFlowTests : IClassFixture<RealAuthQuotesApiFactory>, IDisposable
{
    private const string SecondUserEmail = "second-user@example.com";
    private const string SecondUserPassword = "Password123!";

    private readonly RealAuthQuotesApiFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationFlowTests(RealAuthQuotesApiFactory factory)
    {
        _factory = factory;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();

        if (!dbContext.Users.Any(u => u.Email == UserSeed.Email))
        {
            dbContext.Users.Add(new User
            {
                Email = UserSeed.Email,
                PasswordHash = BCryptNet.BCrypt.HashPassword(UserSeed.Password)
            });
        }

        if (!dbContext.Users.Any(u => u.Email == SecondUserEmail))
        {
            dbContext.Users.Add(new User
            {
                Email = SecondUserEmail,
                PasswordHash = BCryptNet.BCrypt.HashPassword(SecondUserPassword)
            });
        }

        dbContext.SaveChanges();

        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private async Task<TokenResponse> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = email, Password = password });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private async Task<int> CreateQuoteAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new CreateQuoteRequest("Author", "Some quote text"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<Quote>();
        return created!.Id;
    }

    private string CreateExpiredAccessToken(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtSettings = scope.ServiceProvider.GetRequiredService<JwtSettings>();
        var jwtOptions = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;

        var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.SigningKey!);
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("scope", "quotes.write")
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAuthorizationHeader_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Author", "Some quote text"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredAccessToken_ReturnsUnauthorized()
    {
        var expiredToken = CreateExpiredAccessToken(userId: 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new CreateQuoteRequest("Author", "Some quote text"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Edit_WithValidAccessTokenAndOwnership_ReturnsOk()
    {
        var tokens = await LoginAsync(UserSeed.Email, UserSeed.Password);
        var quoteId = await CreateQuoteAsync(tokens.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/quotes/{quoteId}")
        {
            Content = JsonContent.Create(new UpdateQuoteRequest("New Author", "Updated text"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Edit_WithValidAccessTokenButNotOwner_ReturnsForbidden()
    {
        var ownerTokens = await LoginAsync(UserSeed.Email, UserSeed.Password);
        var quoteId = await CreateQuoteAsync(ownerTokens.AccessToken);

        var otherTokens = await LoginAsync(SecondUserEmail, SecondUserPassword);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/quotes/{quoteId}")
        {
            Content = JsonContent.Create(new UpdateQuoteRequest("New Author", "Updated text"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherTokens.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithReusedAlreadyRotatedToken_ReturnsUnauthorized()
    {
        var tokens = await LoginAsync(UserSeed.Email, UserSeed.Password);

        var firstRefresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });
        firstRefresh.EnsureSuccessStatusCode();

        var reuseResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_ConcurrentDuplicateCallsWithSameToken_ExactlyOneSucceeds()
    {
        var tokens = await LoginAsync(UserSeed.Email, UserSeed.Password);
        var tokenA = tokens.RefreshToken;

        var call1 = _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = tokenA });
        var call2 = _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = tokenA });
        var results = await Task.WhenAll(call1, call2);

        var statusCodes = results.Select(r => r.StatusCode).ToList();

        var okCount = statusCodes.Count(s => s == HttpStatusCode.OK);
        var unauthorizedCount = statusCodes.Count(s => s == HttpStatusCode.Unauthorized);

        Assert.Equal(1, okCount);
        Assert.Equal(1, unauthorizedCount);
    }

    [Fact]
    public async Task Refresh_ReproSecondRotation_CheckIfBWorks()
    {
        var tokens = await LoginAsync(UserSeed.Email, UserSeed.Password);
        var tokenA = tokens.RefreshToken;

        var refreshAResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokenA });
        refreshAResponse.EnsureSuccessStatusCode();
        var tokenB = (await refreshAResponse.Content.ReadFromJsonAsync<TokenResponse>())!.RefreshToken;

        var refreshBResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokenB });

        Assert.Equal(HttpStatusCode.OK, refreshBResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithTokenRevokedViaLogout_ReturnsUnauthorized()
    {
        var tokens = await LoginAsync(UserSeed.Email, UserSeed.Password);

        var logoutResponse = await _client.PostAsJsonAsync(
            "/api/auth/logout",
            new LogoutRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }
}

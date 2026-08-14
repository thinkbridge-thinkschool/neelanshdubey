using System.Net;
using System.Net.Http.Json;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// SQL-Server-backed counterpart to a subset of AuthEndpointsTests, run
// against a real mssql/server:2022-latest container via Testcontainers
// instead of SQLite (see SqlServerContainerFixture / SqlServerIntegrationTestBase).
[Collection(SqlServerCollection.Name)]
public class SqlServerAuthEndpointsTests : SqlServerIntegrationTestBase
{
    public SqlServerAuthEndpointsTests(SqlServerContainerFixture containerFixture)
        : base(containerFixture)
    {
    }

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

    // The login lookup (AuthEndpointExtensions.MapAuthEndpoints) compares
    // emails with a plain "==", relying entirely on whatever collation the
    // underlying database applies to string equality. SQLite's default
    // collation for TEXT columns is BINARY (case-sensitive), so a
    // differently-cased email is a guaranteed miss there. SQL Server's
    // default collation (SQL_Latin1_General_CP1_CI_AS) is case-insensitive,
    // so the same lookup matches here - a genuine, provider-driven behavior
    // difference the SQLite suite cannot observe.
    [Fact]
    public async Task Login_WithDifferentCasedEmail_SucceedsBecauseSqlServerCollationIsCaseInsensitive()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = UserSeed.Email.ToUpperInvariant(), Password = UserSeed.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterFakeClockAdvancesPastExpiry_ReturnsUnauthorized()
    {
        var tokens = await LoginAsync();

        // The refresh token is valid for 7 days from the moment it was
        // issued (measured against the fake clock). Jumping the clock 8
        // days forward proves the expiry check without any real waiting -
        // and exercises real datetimeoffset storage/comparison in SQL
        // Server rather than SQLite's TEXT-based representation.
        Clock.UtcNow = Clock.UtcNow.AddDays(8);

        var response = await Client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

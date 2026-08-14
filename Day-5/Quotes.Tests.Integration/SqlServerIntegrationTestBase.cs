using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// The SQL Server container itself is shared across every test in the
// "SqlServer" collection (see SqlServerContainerFixture) because starting a
// new container per test would be far too slow. Isolation instead comes
// from each test creating and dropping its own uniquely-named database on
// that shared server: InitializeAsync/DisposeAsync run once per test method
// (this is a plain IAsyncLifetime, not an IClassFixture), so no two tests
// ever read or write the same database, regardless of xUnit's execution
// order. A shared-schema-with-rollback-transaction approach was considered
// and rejected: each HTTP call through the WebApplicationFactory opens its
// own DbContext/connection, so keeping one ambient transaction alive across
// several independent requests per test would require distributed
// transaction coordination for no real benefit over just using a separate
// database.
public abstract class SqlServerIntegrationTestBase : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _containerFixture;
    private string _databaseName = string.Empty;

    protected SqlServerIntegrationTestFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected FakeClock Clock => Factory.Clock;

    protected SqlServerIntegrationTestBase(SqlServerContainerFixture containerFixture)
    {
        _containerFixture = containerFixture;
    }

    public async Task InitializeAsync()
    {
        _databaseName = $"QuotesApiTests_{Guid.NewGuid():N}";
        var connectionString = await _containerFixture.CreateDatabaseAsync(_databaseName);

        Factory = new SqlServerIntegrationTestFactory(connectionString);
        await Factory.InitializeDatabaseAsync();
        await SeedDefaultUserAsync();

        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();

        await _containerFixture.DropDatabaseAsync(_databaseName);
    }

    private async Task SeedDefaultUserAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Users.Add(new User
        {
            Email = UserSeed.Email,
            PasswordHash = BCryptNet.BCrypt.HashPassword(UserSeed.Password)
        });

        await dbContext.SaveChangesAsync();
    }

    protected async Task<TokenResponse> LoginAsync(
        string? email = null,
        string? password = null)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                Email = email ?? UserSeed.Email,
                Password = password ?? UserSeed.Password
            });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    protected static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string url,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return request;
    }

    protected async Task<Quote> CreateQuoteAsync(
        string accessToken,
        string author = "Marcus Aurelius",
        string text = "You have power over your mind, not outside events.")
    {
        var request = AuthorizedRequest(HttpMethod.Post, "/api/quotes", accessToken);
        request.Content = JsonContent.Create(new CreateQuoteRequest(author, text));

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Quote>())!;
    }
}

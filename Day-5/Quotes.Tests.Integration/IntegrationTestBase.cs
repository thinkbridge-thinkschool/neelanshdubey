using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// Deliberately NOT an IClassFixture: xUnit creates a fresh instance of the
// derived test class for every [Fact], so InitializeAsync/DisposeAsync run
// once per test method. That gives each test its own WebApplicationFactory,
// its own SQLite ":memory:" connection/database, and its own HttpClient -
// tests can run in any order, or in parallel, without touching each other's
// data.
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IntegrationTestFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected FakeClock Clock => Factory.Clock;

    public async Task InitializeAsync()
    {
        Factory = new IntegrationTestFactory();
        await Factory.InitializeDatabaseAsync();
        await SeedDefaultUserAsync();

        Client = Factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();

        return Task.CompletedTask;
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

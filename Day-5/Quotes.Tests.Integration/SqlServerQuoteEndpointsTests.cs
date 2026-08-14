using System.Net;
using System.Net.Http.Json;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// SQL-Server-backed counterpart to a subset of QuoteEndpointsTests, run
// against a real mssql/server:2022-latest container via Testcontainers
// instead of SQLite (see SqlServerContainerFixture / SqlServerIntegrationTestBase).
[Collection(SqlServerCollection.Name)]
public class SqlServerQuoteEndpointsTests : SqlServerIntegrationTestBase
{
    public SqlServerQuoteEndpointsTests(SqlServerContainerFixture containerFixture)
        : base(containerFixture)
    {
    }

    [Fact]
    public async Task CreateQuote_WithValidToken_ReturnsCreated()
    {
        var tokens = await LoginAsync();

        var request = AuthorizedRequest(HttpMethod.Post, "/api/quotes", tokens.AccessToken);
        request.Content = JsonContent.Create(new CreateQuoteRequest("Seneca", "It is not that we have a short time to live, but that we waste a lot of it."));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<Quote>();
        Assert.Equal("Seneca", created!.Author);
        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task GetQuotes_ReturnsOkAndIncludesCreatedQuote()
    {
        var tokens = await LoginAsync();
        var created = await CreateQuoteAsync(tokens.AccessToken);

        var response = await Client.GetAsync("/api/quotes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quotes = await response.Content.ReadFromJsonAsync<List<Quote>>();
        Assert.Contains(quotes!, q => q.Id == created.Id);
    }

    [Fact]
    public async Task UpdateQuote_WithValidTokenAndOwnership_ReturnsOkWithUpdatedFields()
    {
        var tokens = await LoginAsync();
        var created = await CreateQuoteAsync(tokens.AccessToken);

        var request = AuthorizedRequest(HttpMethod.Put, $"/api/quotes/{created.Id}", tokens.AccessToken);
        request.Content = JsonContent.Create(new UpdateQuoteRequest("Epictetus", "It's not what happens to you, but how you react to it that matters."));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<Quote>();
        Assert.Equal("Epictetus", updated!.Author);
    }

    [Fact]
    public async Task DeleteQuote_WithValidTokenAndOwnership_ReturnsNoContentAndRemovesQuote()
    {
        var tokens = await LoginAsync();
        var created = await CreateQuoteAsync(tokens.AccessToken);

        var deleteResponse = await Client.SendAsync(
            AuthorizedRequest(HttpMethod.Delete, $"/api/quotes/{created.Id}", tokens.AccessToken));

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/quotes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}

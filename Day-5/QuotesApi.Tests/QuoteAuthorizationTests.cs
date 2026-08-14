using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Tests;

public class QuoteAuthorizationTests : IClassFixture<QuotesApiFactory>, IDisposable
{
    private readonly QuotesApiFactory _factory;
    private readonly HttpClient _client;

    public QuoteAuthorizationTests(QuotesApiFactory factory)
    {
        _factory = factory;

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, int userId, string? scope = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());

        if (scope is not null)
        {
            request.Headers.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return request;
    }

    private async Task<int> CreateQuoteAsync(int ownerId)
    {
        var request = BuildRequest(HttpMethod.Post, "/api/quotes", ownerId);
        request.Content = JsonContent.Create(new CreateQuoteRequest("Author", "Some quote text"));

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<Quote>();
        return created!.Id;
    }

    [Fact]
    public async Task Edit_WithoutScopeClaim_ReturnsForbidden()
    {
        var quoteId = await CreateQuoteAsync(ownerId: 1);

        var request = BuildRequest(HttpMethod.Put, $"/api/quotes/{quoteId}", userId: 1);
        request.Content = JsonContent.Create(new UpdateQuoteRequest("New Author", "Updated text"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Edit_WithScopeClaim_Succeeds()
    {
        var quoteId = await CreateQuoteAsync(ownerId: 1);

        var request = BuildRequest(HttpMethod.Put, $"/api/quotes/{quoteId}", userId: 1, scope: "quotes.write");
        request.Content = JsonContent.Create(new UpdateQuoteRequest("New Author", "Updated text"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<Quote>();
        Assert.Equal("New Author", updated!.Author);
        Assert.Equal("Updated text", updated.Text);
    }

    [Fact]
    public async Task Edit_QuoteNotOwnedByCaller_ReturnsForbidden()
    {
        var quoteId = await CreateQuoteAsync(ownerId: 1);

        var request = BuildRequest(HttpMethod.Put, $"/api/quotes/{quoteId}", userId: 2, scope: "quotes.write");
        request.Content = JsonContent.Create(new UpdateQuoteRequest("New Author", "Updated text"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_QuoteNotOwnedByCaller_ReturnsForbidden()
    {
        var quoteId = await CreateQuoteAsync(ownerId: 1);

        var request = BuildRequest(HttpMethod.Delete, $"/api/quotes/{quoteId}", userId: 2);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutUserIdClaim_ReturnsForbidden()
    {
        var quoteId = await CreateQuoteAsync(ownerId: 1);

        // Authenticated (TestAuthHandler always succeeds), but with no Sub/
        // NameIdentifier claim at all, so GetUserId() returns null and the
        // same-owner requirement can never succeed for any resource.
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/quotes/{quoteId}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnQuote_Succeeds()
    {
        var quoteId = await CreateQuoteAsync(ownerId: 1);

        var request = BuildRequest(HttpMethod.Delete, $"/api/quotes/{quoteId}", userId: 1);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/quotes/{quoteId}", userId: 1));
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}

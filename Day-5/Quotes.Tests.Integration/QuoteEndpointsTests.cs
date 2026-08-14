using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// Exercises the quotes CRUD endpoints through the real authorization
// pipeline (JWT bearer auth, "can-edit-quotes" scope policy, and the
// resource-based "can-delete-own-quote" ownership policy) end to end.
public class QuoteEndpointsTests : IntegrationTestBase
{
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
    public async Task GetQuoteById_ReturnsOkWithMatchingQuote()
    {
        var tokens = await LoginAsync();
        var created = await CreateQuoteAsync(tokens.AccessToken);

        var response = await Client.GetAsync($"/api/quotes/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fetched = await response.Content.ReadFromJsonAsync<Quote>();
        Assert.Equal(created.Author, fetched!.Author);
        Assert.Equal(created.Text, fetched.Text);
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
    public async Task CreateQuote_WithoutToken_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Author", "Some quote text"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithEmptyAuthorAndText_ReturnsValidationProblemDetails()
    {
        var tokens = await LoginAsync();

        var request = AuthorizedRequest(HttpMethod.Post, "/api/quotes", tokens.AccessToken);
        request.Content = JsonContent.Create(new CreateQuoteRequest(string.Empty, string.Empty));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
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

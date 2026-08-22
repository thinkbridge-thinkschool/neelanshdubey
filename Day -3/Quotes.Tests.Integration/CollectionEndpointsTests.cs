using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// Exercises the collection endpoints through the real authorization pipeline
// (JWT bearer auth + the resource-based "can-manage-own-collection"
// ownership policy), mirroring QuoteEndpointsTests.
//
// Collection deserializes into a local DTO rather than QuotesApi.Models.
// Collection itself: Collection's constructor/setters are private by design
// (see Models/Collection.cs), so System.Text.Json has no accessible
// constructor to deserialize a JSON response back into it. That's a
// deliberate encapsulation choice on the domain type, not something to
// relax just to make tests convenient.
public class CollectionEndpointsTests : IntegrationTestBase
{
    private sealed record CollectionResponse(Guid Id, string Name, int OwnerId, List<CollectionItemResponse> Items);

    private sealed record CollectionItemResponse(int QuoteId, DateTimeOffset AddedAt);

    private async Task<CollectionResponse> CreateCollectionAsync(string accessToken, string name = "Stoic Favorites")
    {
        var request = AuthorizedRequest(HttpMethod.Post, "/api/collections", accessToken);
        request.Content = JsonContent.Create(new CreateCollectionRequest(name));

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CollectionResponse>())!;
    }

    [Fact]
    public async Task CreateCollection_WithValidToken_ReturnsCreated()
    {
        var tokens = await LoginAsync();

        var request = AuthorizedRequest(HttpMethod.Post, "/api/collections", tokens.AccessToken);
        request.Content = JsonContent.Create(new CreateCollectionRequest("Stoic Favorites"));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CollectionResponse>();
        Assert.Equal("Stoic Favorites", created!.Name);
        Assert.Empty(created.Items);
    }

    [Fact]
    public async Task AddItem_ValidQuoteId_ReturnsOkWithAppendedItem()
    {
        var tokens = await LoginAsync();
        var collection = await CreateCollectionAsync(tokens.AccessToken);
        var quote = await CreateQuoteAsync(tokens.AccessToken);

        var request = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", tokens.AccessToken);
        request.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<CollectionResponse>();
        Assert.Contains(updated!.Items, i => i.QuoteId == quote.Id);
    }

    [Fact]
    public async Task AddItem_Over50Items_ReturnsBadRequestProblemDetails()
    {
        var tokens = await LoginAsync();
        var collection = await CreateCollectionAsync(tokens.AccessToken);

        for (var i = 0; i < 50; i++)
        {
            var quote = await CreateQuoteAsync(tokens.AccessToken, text: $"Quote number {i}");

            var addRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", tokens.AccessToken);
            addRequest.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));

            var addResponse = await Client.SendAsync(addRequest);
            addResponse.EnsureSuccessStatusCode();
        }

        var overLimitQuote = await CreateQuoteAsync(tokens.AccessToken, text: "One too many");
        var overLimitRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", tokens.AccessToken);
        overLimitRequest.Content = JsonContent.Create(new AddCollectionItemRequest(overLimitQuote.Id));

        var response = await Client.SendAsync(overLimitRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
        Assert.Equal("A collection cannot contain more than 50 items.", problem.Detail);
    }

    [Fact]
    public async Task AddItem_DuplicateQuoteId_ReturnsBadRequestProblemDetails()
    {
        var tokens = await LoginAsync();
        var collection = await CreateCollectionAsync(tokens.AccessToken);
        var quote = await CreateQuoteAsync(tokens.AccessToken);

        var firstRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", tokens.AccessToken);
        firstRequest.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));
        (await Client.SendAsync(firstRequest)).EnsureSuccessStatusCode();

        var secondRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", tokens.AccessToken);
        secondRequest.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));
        var response = await Client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("This quote is already in the collection.", problem!.Detail);
    }

    [Fact]
    public async Task RemoveItem_ExistingQuoteId_ReturnsNoContentAndRemovesItem()
    {
        var tokens = await LoginAsync();
        var collection = await CreateCollectionAsync(tokens.AccessToken);
        var quote = await CreateQuoteAsync(tokens.AccessToken);

        var addRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", tokens.AccessToken);
        addRequest.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));
        (await Client.SendAsync(addRequest)).EnsureSuccessStatusCode();

        var deleteResponse = await Client.SendAsync(
            AuthorizedRequest(HttpMethod.Delete, $"/api/collections/{collection.Id}/items/{quote.Id}", tokens.AccessToken));

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/collections/{collection.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<CollectionResponse>();
        Assert.DoesNotContain(fetched!.Items, i => i.QuoteId == quote.Id);
    }

    [Fact]
    public async Task RemoveItem_QuoteIdNotPresent_ReturnsBadRequestProblemDetails()
    {
        var tokens = await LoginAsync();
        var collection = await CreateCollectionAsync(tokens.AccessToken);

        var response = await Client.SendAsync(
            AuthorizedRequest(HttpMethod.Delete, $"/api/collections/{collection.Id}/items/999999", tokens.AccessToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("This quote is not in the collection.", problem!.Detail);
    }

    [Fact]
    public async Task AddItem_AnotherUsersCollection_ReturnsForbidden()
    {
        var owner = await LoginAsync();
        var collection = await CreateCollectionAsync(owner.AccessToken);
        var quote = await CreateQuoteAsync(owner.AccessToken);

        var otherUserTokens = await RegisterAndLoginOtherUserAsync();

        var request = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", otherUserTokens.AccessToken);
        request.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<TokenResponse> RegisterAndLoginOtherUserAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        const string email = "other-owner@example.com";
        const string password = "P@ssword123!";

        dbContext.Users.Add(new User
        {
            Email = email,
            PasswordHash = BCryptNet.BCrypt.HashPassword(password)
        });

        await dbContext.SaveChangesAsync();

        return await LoginAsync(email, password);
    }
}

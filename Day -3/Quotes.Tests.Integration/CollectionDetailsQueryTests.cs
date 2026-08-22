using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;

namespace Quotes.Tests.Integration;

// Exercises the read model's query handler directly (bypassing HTTP) so it
// can assert on the exact SQL round-trip count, plus the HTTP endpoint that
// wraps it.
public class CollectionDetailsQueryTests : IntegrationTestBase
{
    private sealed record CollectionResponse(Guid Id, string Name, int OwnerId);

    private async Task<(Guid CollectionId, Quote Quote1, Quote Quote2)> SeedCollectionWithTwoQuotesAsync(
        string accessToken)
    {
        var quote1 = await CreateQuoteAsync(accessToken, author: "Marcus Aurelius", text: "You have power over your mind.");
        var quote2 = await CreateQuoteAsync(accessToken, author: "Seneca", text: "Luck is what happens when preparation meets opportunity.");

        var createRequest = AuthorizedRequest(HttpMethod.Post, "/api/collections", accessToken);
        createRequest.Content = JsonContent.Create(new CreateCollectionRequest("Stoic Favorites"));
        var createResponse = await Client.SendAsync(createRequest);
        var collection = (await createResponse.Content.ReadFromJsonAsync<CollectionResponse>())!;

        foreach (var quote in new[] { quote1, quote2 })
        {
            var addRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", accessToken);
            addRequest.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));
            (await Client.SendAsync(addRequest)).EnsureSuccessStatusCode();
        }

        return (collection.Id, quote1, quote2);
    }

    [Fact]
    public async Task HandleAsync_CollectionWithItems_ReturnsFlattenedShapeInOneQuery()
    {
        var tokens = await LoginAsync();
        var (collectionId, quote1, quote2) = await SeedCollectionWithTwoQuotesAsync(tokens.AccessToken);

        var connection = Factory.Services.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        var counter = new CommandCountInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;

        using var countedDb = new AppDbContext(options);
        var handler = new GetCollectionDetailsQueryHandler(countedDb);

        var result = await handler.HandleAsync(
            new GetCollectionDetailsQuery(collectionId),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(collectionId, result!.CollectionId);
        Assert.Equal("Stoic Favorites", result.CollectionName);
        Assert.Equal(2, result.ItemCount);
        Assert.Contains(result.Items, i => i.QuoteId == quote1.Id && i.QuoteText == quote1.Text && i.AuthorName == quote1.Author);
        Assert.Contains(result.Items, i => i.QuoteId == quote2.Id && i.QuoteText == quote2.Text && i.AuthorName == quote2.Author);

        // No tracked entities of any kind - this is a pure projection.
        Assert.Empty(countedDb.ChangeTracker.Entries());

        // Exactly one SQL statement reached the database for the whole
        // Collections -> CollectionItems -> Quotes join.
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task HandleAsync_CollectionWithNoItems_ReturnsEmptyItemsNotNull()
    {
        var tokens = await LoginAsync();

        var createRequest = AuthorizedRequest(HttpMethod.Post, "/api/collections", tokens.AccessToken);
        createRequest.Content = JsonContent.Create(new CreateCollectionRequest("Empty Collection"));
        var createResponse = await Client.SendAsync(createRequest);
        var collection = (await createResponse.Content.ReadFromJsonAsync<CollectionResponse>())!;

        using var scope = Factory.Services.CreateScope();
        var handler = new GetCollectionDetailsQueryHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        var result = await handler.HandleAsync(
            new GetCollectionDetailsQuery(collection.Id),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result!.ItemCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_CollectionDoesNotExist_ReturnsNull()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = new GetCollectionDetailsQueryHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        var result = await handler.HandleAsync(
            new GetCollectionDetailsQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDetails_ExistingCollection_ReturnsOkWithFlattenedShape()
    {
        var tokens = await LoginAsync();
        var (collectionId, quote1, _) = await SeedCollectionWithTwoQuotesAsync(tokens.AccessToken);

        var response = await Client.GetAsync($"/api/collections/{collectionId}/details");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<CollectionDetailsReadModel>();
        Assert.NotNull(details);
        Assert.Equal(collectionId, details!.CollectionId);
        Assert.Equal(2, details.ItemCount);
        Assert.Contains(details.Items, i => i.QuoteId == quote1.Id);
    }

    [Fact]
    public async Task GetDetails_CollectionDoesNotExist_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"/api/collections/{Guid.NewGuid()}/details");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

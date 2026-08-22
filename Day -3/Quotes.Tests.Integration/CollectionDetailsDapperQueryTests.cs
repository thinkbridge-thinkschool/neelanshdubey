using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;

namespace Quotes.Tests.Integration;

// Correctness check that justifies trusting the timing comparison in
// CollectionReadModelBenchmarkTests: the Dapper handler must return the same
// CollectionDetailsReadModel shape/values as the EF handler for the same
// collection, not just "something fast."
public class CollectionDetailsDapperQueryTests : IntegrationTestBase
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
    public async Task HandleAsync_CollectionWithItems_MatchesEfReadModelExactly()
    {
        var tokens = await LoginAsync();
        var (collectionId, _, _) = await SeedCollectionWithTwoQuotesAsync(tokens.AccessToken);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var efResult = await new GetCollectionDetailsQueryHandler(db)
            .HandleAsync(new GetCollectionDetailsQuery(collectionId), CancellationToken.None);

        var dapperResult = await new GetCollectionDetailsDapperQueryHandler(db)
            .HandleAsync(new GetCollectionDetailsDapperQuery(collectionId), CancellationToken.None);

        Assert.NotNull(efResult);
        Assert.NotNull(dapperResult);

        Assert.Equal(efResult!.CollectionId, dapperResult!.CollectionId);
        Assert.Equal(efResult.CollectionName, dapperResult.CollectionName);
        Assert.Equal(efResult.ItemCount, dapperResult.ItemCount);

        var efItems = efResult.Items.OrderBy(i => i.QuoteId).ToList();
        var dapperItems = dapperResult.Items.OrderBy(i => i.QuoteId).ToList();

        Assert.Equal(efItems.Count, dapperItems.Count);
        for (var i = 0; i < efItems.Count; i++)
        {
            Assert.Equal(efItems[i].QuoteId, dapperItems[i].QuoteId);
            Assert.Equal(efItems[i].QuoteText, dapperItems[i].QuoteText);
            Assert.Equal(efItems[i].AuthorName, dapperItems[i].AuthorName);
            Assert.Equal(efItems[i].AddedAtUtc, dapperItems[i].AddedAtUtc);
        }
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
        var handler = new GetCollectionDetailsDapperQueryHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        var result = await handler.HandleAsync(
            new GetCollectionDetailsDapperQuery(collection.Id),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result!.ItemCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_CollectionDoesNotExist_ReturnsNull()
    {
        using var scope = Factory.Services.CreateScope();
        var handler = new GetCollectionDetailsDapperQueryHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        var result = await handler.HandleAsync(
            new GetCollectionDetailsDapperQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDetailsDapper_ExistingCollection_ReturnsOkWithFlattenedShape()
    {
        var tokens = await LoginAsync();
        var (collectionId, quote1, _) = await SeedCollectionWithTwoQuotesAsync(tokens.AccessToken);

        var response = await Client.GetAsync($"/api/collections/{collectionId}/details/dapper");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<CollectionDetailsReadModel>();
        Assert.NotNull(details);
        Assert.Equal(collectionId, details!.CollectionId);
        Assert.Equal(2, details.ItemCount);
        Assert.Contains(details.Items, i => i.QuoteId == quote1.Id);
    }

    [Fact]
    public async Task GetDetailsDapper_CollectionDoesNotExist_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"/api/collections/{Guid.NewGuid()}/details/dapper");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

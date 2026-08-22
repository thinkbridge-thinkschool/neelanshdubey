using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;
using Xunit.Abstractions;

namespace Quotes.Tests.Integration;

// Part C: proves the write-model path and the read-model path are actually
// different roads, not just different names for the same query - by timing
// what a caller who only has the write side available would have to do to
// render a "collection details" screen (load the tracked aggregate, then
// fetch each quote one at a time to flatten it) against the read model's
// single projected query, on a collection with 30 items.
public class CollectionReadModelBenchmarkTests : IntegrationTestBase
{
    private const int MeasuredIterations = 5;

    private sealed record CollectionResponse(Guid Id, string Name, int OwnerId);

    private sealed record BenchmarkResult(string Label, double AvgMs, int QueryCount);

    private async Task<Guid> SeedCollectionWithItemsAsync(string accessToken, int itemCount)
    {
        var createRequest = AuthorizedRequest(HttpMethod.Post, "/api/collections", accessToken);
        createRequest.Content = JsonContent.Create(new CreateCollectionRequest("Benchmark Collection"));
        var createResponse = await Client.SendAsync(createRequest);
        var collection = (await createResponse.Content.ReadFromJsonAsync<CollectionResponse>())!;

        for (var i = 0; i < itemCount; i++)
        {
            var quote = await CreateQuoteAsync(accessToken, author: $"Author {i}", text: $"Quote text number {i}");

            var addRequest = AuthorizedRequest(HttpMethod.Post, $"/api/collections/{collection.Id}/items", accessToken);
            addRequest.Content = JsonContent.Create(new AddCollectionItemRequest(quote.Id));
            (await Client.SendAsync(addRequest)).EnsureSuccessStatusCode();
        }

        return collection.Id;
    }

    // What a caller without the read model has to do: load the tracked
    // Collection aggregate (as the write side would), then fetch each
    // referenced Quote one at a time - since Collection only ever knows
    // QuoteIds, never quote text/author - and flatten by hand. This is the
    // N+1 the read model exists to avoid.
    private static async Task<(CollectionDetailsReadModel Model, int QueryCount)> LoadAggregateAndFlattenManuallyAsync(
        DbContextOptions<AppDbContext> options,
        Guid collectionId)
    {
        var counter = new CommandCountInterceptor();
        var countedOptions = new DbContextOptionsBuilder<AppDbContext>(options)
            .AddInterceptors(counter)
            .Options;

        using var db = new AppDbContext(countedOptions);

        var collection = await db.Collections.FirstAsync(c => c.Id == collectionId);

        var items = new List<CollectionItemReadModel>();
        foreach (var item in collection.Items)
        {
            var quote = await db.Quotes.AsNoTracking().FirstAsync(q => q.Id == item.QuoteId);
            items.Add(new CollectionItemReadModel(quote.Id, quote.Text, quote.Author, item.AddedAt));
        }

        var model = new CollectionDetailsReadModel(collection.Id, collection.Name, items.Count, items);
        return (model, counter.Count);
    }

    private static async Task<(CollectionDetailsReadModel? Model, int QueryCount)> LoadViaReadModelAsync(
        DbContextOptions<AppDbContext> options,
        Guid collectionId)
    {
        var counter = new CommandCountInterceptor();
        var countedOptions = new DbContextOptionsBuilder<AppDbContext>(options)
            .AddInterceptors(counter)
            .Options;

        using var db = new AppDbContext(countedOptions);
        var handler = new GetCollectionDetailsQueryHandler(db);

        var model = await handler.HandleAsync(new GetCollectionDetailsQuery(collectionId), CancellationToken.None);
        return (model, counter.Count);
    }

    private readonly ITestOutputHelper _output;

    public CollectionReadModelBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Benchmark_30ItemCollection_ReadModelIsOneQueryAggregateFlattenIsNPlus1()
    {
        var tokens = await LoginAsync();
        const int itemCount = 30;
        var collectionId = await SeedCollectionWithItemsAsync(tokens.AccessToken, itemCount);

        var baseOptions = Factory.Services.GetRequiredService<DbContextOptions<AppDbContext>>();

        // Warmup (discarded): pays for query-plan caching so it doesn't
        // pollute the measured runs, same as the Day 10 AsNoTracking benchmark.
        await LoadAggregateAndFlattenManuallyAsync(baseOptions, collectionId);
        await LoadViaReadModelAsync(baseOptions, collectionId);

        var naive = await RunBenchmarkAsync(
            "Load aggregate + manual flatten (N+1)",
            () => LoadAggregateAndFlattenManuallyAsync(baseOptions, collectionId));

        var readModel = await RunBenchmarkAsync(
            "Read model (projected, single query)",
            async () =>
            {
                var (model, queryCount) = await LoadViaReadModelAsync(baseOptions, collectionId);
                return (model!, queryCount);
            });

        _output.WriteLine("");
        _output.WriteLine($"-- Collection details, {itemCount} items, {MeasuredIterations} measured iterations (1 warmup discarded) --");
        _output.WriteLine($"{"Variant",-42}{"Avg ms",10}{"SQL queries",14}");
        _output.WriteLine(new string('-', 66));
        foreach (var r in new[] { naive, readModel })
        {
            _output.WriteLine($"{r.Label,-42}{r.AvgMs,10:F2}{r.QueryCount,14}");
        }

        // The read model's query count never changes with N; the naive path
        // always does 1 (collection) + N (one per item) round trips.
        Assert.Equal(1, readModel.QueryCount);
        Assert.Equal(itemCount + 1, naive.QueryCount);
        Assert.True(
            readModel.AvgMs < naive.AvgMs,
            $"Expected the single-query read model ({readModel.AvgMs:F2}ms) to beat the {itemCount + 1}-query naive flatten ({naive.AvgMs:F2}ms).");
    }

    private static async Task<BenchmarkResult> RunBenchmarkAsync(
        string label,
        Func<Task<(CollectionDetailsReadModel Model, int QueryCount)>> action)
    {
        var times = new List<double>(MeasuredIterations);
        var sw = new Stopwatch();
        var queryCount = 0;

        for (var i = 0; i < MeasuredIterations; i++)
        {
            sw.Restart();
            var (_, count) = await action();
            sw.Stop();

            times.Add(sw.Elapsed.TotalMilliseconds);
            queryCount = count;
        }

        return new BenchmarkResult(label, times.Average(), queryCount);
    }
}

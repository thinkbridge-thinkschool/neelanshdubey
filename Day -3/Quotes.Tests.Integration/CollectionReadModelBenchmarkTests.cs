using System.Diagnostics;
using System.Net.Http.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;
using Xunit.Abstractions;

namespace Quotes.Tests.Integration;

// Part C (Task 1) proved the write-model path and the read-model path are
// different roads by timing a naive N+1 flatten against the EF read model.
// Part B (Task 2) extends that into a three-way comparison: the same naive
// path (kept for context), the EF projection, and a hand-written Dapper
// query for the identical "collection details" shape - the actual question
// this task asks is EF-projection vs Dapper, not naive-vs-anything.
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

    // EF's DbCommandInterceptor never fires for Dapper's calls - confirmed
    // empirically with a probe: running a Dapper query against the same
    // connection an interceptor-wired AppDbContext was watching left
    // counter.Count at 0, because Dapper calls CreateCommand()/ExecuteReader
    // directly on the connection and never touches EF's RelationalCommand
    // pipeline the interceptor hooks into. The alternative - wrapping the
    // connection in a full EF AppDbContext (UseSqlite(proxyConnection)) so
    // the interceptor has something EF-shaped to watch - was also measured:
    // a single such call took over 5 minutes (EF's Sqlite provider behaves
    // pathologically when handed a non-SqliteConnection DbConnection), which
    // would make the timed loop meaningless. CountingDbConnection instead
    // counts at the raw ADO.NET level with no EF involved at all - confirmed
    // fast (low tens of ms) - run directly against the handler's own SQL
    // text (GetCollectionDetailsDapperQueryHandler.Sql, made internal for
    // exactly this). The handler issues that one static statement with no
    // per-item loop, so the count can't vary by item count or iteration;
    // it's measured once per test run rather than re-proving it on every
    // timed call, the same invariant the EF variant's count already relies
    // on (always 1, regardless of collection size).
    private static async Task<int> MeasureDapperQueryCountAsync(
        DbContextOptions<AppDbContext> options,
        Guid collectionId)
    {
        using var db = new AppDbContext(options);
        var counting = new CountingDbConnection(db.Database.GetDbConnection());

        await counting.QueryAsync(
            GetCollectionDetailsDapperQueryHandler.Sql,
            new { CollectionId = collectionId });

        return counting.Count;
    }

    private static async Task<(CollectionDetailsReadModel Model, int QueryCount)> LoadViaDapperReadModelAsync(
        DbContextOptions<AppDbContext> options,
        Guid collectionId,
        int measuredQueryCount)
    {
        using var db = new AppDbContext(options);
        var handler = new GetCollectionDetailsDapperQueryHandler(db);

        var model = await handler.HandleAsync(new GetCollectionDetailsDapperQuery(collectionId), CancellationToken.None);
        return (model!, measuredQueryCount);
    }

    private readonly ITestOutputHelper _output;

    public CollectionReadModelBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // 30 items matches Task 1's benchmark size; 50 - the write model's own
    // cap (Collection.AddItem throws DomainException past 50 items, so 200
    // isn't reachable through the real write path at all) - is included
    // because the gap between EF and Dapper, unlike the read-model-vs-naive
    // gap, is small enough at 30 items that it can be noise, and the
    // largest collection the domain allows is where a real per-query
    // overhead difference would most plausibly show up.
    [Theory]
    [InlineData(30)]
    [InlineData(50)]
    public async Task Benchmark_CollectionDetails_EfVsDapperVsNaive(int itemCount)
    {
        var tokens = await LoginAsync();
        var collectionId = await SeedCollectionWithItemsAsync(tokens.AccessToken, itemCount);

        var baseOptions = Factory.Services.GetRequiredService<DbContextOptions<AppDbContext>>();

        var dapperQueryCount = await MeasureDapperQueryCountAsync(baseOptions, collectionId);

        // Warmup (discarded): pays for query-plan caching so it doesn't
        // pollute the measured runs, same as the Day 10 AsNoTracking benchmark.
        await LoadAggregateAndFlattenManuallyAsync(baseOptions, collectionId);
        await LoadViaReadModelAsync(baseOptions, collectionId);
        await LoadViaDapperReadModelAsync(baseOptions, collectionId, dapperQueryCount);

        var naive = await RunBenchmarkAsync(
            "Load aggregate + manual flatten (N+1)",
            () => LoadAggregateAndFlattenManuallyAsync(baseOptions, collectionId));

        var efReadModel = await RunBenchmarkAsync(
            "EF read model (projected, single query)",
            async () =>
            {
                var (model, queryCount) = await LoadViaReadModelAsync(baseOptions, collectionId);
                return (model!, queryCount);
            });

        var dapperReadModel = await RunBenchmarkAsync(
            "Dapper read model (raw SQL, single query)",
            () => LoadViaDapperReadModelAsync(baseOptions, collectionId, dapperQueryCount));

        _output.WriteLine("");
        _output.WriteLine($"-- Collection details, {itemCount} items, {MeasuredIterations} measured iterations (1 warmup discarded) --");
        _output.WriteLine($"{"Variant",-42}{"Avg ms",10}{"SQL queries",14}");
        _output.WriteLine(new string('-', 66));
        foreach (var r in new[] { naive, efReadModel, dapperReadModel })
        {
            _output.WriteLine($"{r.Label,-42}{r.AvgMs,10:F2}{r.QueryCount,14}");
        }

        // Query counts are fixed regardless of item count for both
        // single-statement variants; the naive path is always 1 + itemCount.
        Assert.Equal(1, efReadModel.QueryCount);
        Assert.Equal(1, dapperReadModel.QueryCount);
        Assert.Equal(itemCount + 1, naive.QueryCount);
        Assert.True(
            efReadModel.AvgMs < naive.AvgMs,
            $"Expected the EF read model ({efReadModel.AvgMs:F2}ms) to beat the {itemCount + 1}-query naive flatten ({naive.AvgMs:F2}ms).");
        Assert.True(
            dapperReadModel.AvgMs < naive.AvgMs,
            $"Expected the Dapper read model ({dapperReadModel.AvgMs:F2}ms) to beat the {itemCount + 1}-query naive flatten ({naive.AvgMs:F2}ms).");
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

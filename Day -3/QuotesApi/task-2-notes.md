# Day 12, Task 2: When to reach for Dapper

EF is the default read path (Task 1's `GetCollectionDetailsQueryHandler`).
This task reimplements the same "collection details" read with Dapper,
times both head-to-head against the naive N+1 baseline, and writes down the
rule for when the switch is actually worth it.

## Part A - EF query handler (from Task 1, for reference)

[`Queries/GetCollectionDetailsQueryHandler.cs`](Queries/GetCollectionDetailsQueryHandler.cs):

```csharp
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

public class GetCollectionDetailsQueryHandler
{
    private readonly AppDbContext _db;

    public GetCollectionDetailsQueryHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CollectionDetailsReadModel?> HandleAsync(
        GetCollectionDetailsQuery query,
        CancellationToken cancellationToken)
    {
        var flat = await _db.Collections
            .AsNoTracking()
            .Where(c => c.Id == query.CollectionId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                Items = c.Items
                    .Join(
                        _db.Quotes.AsNoTracking(),
                        item => item.QuoteId,
                        quote => quote.Id,
                        (item, quote) => new CollectionItemReadModel(
                            quote.Id,
                            quote.Text,
                            quote.Author,
                            item.AddedAt))
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (flat is null)
            return null;

        return new CollectionDetailsReadModel(
            flat.Id,
            flat.Name,
            flat.Items.Count,
            flat.Items);
    }
}
```

One SQL statement (LEFT JOIN CollectionItems, LEFT JOIN Quotes), confirmed
via `DbCommandInterceptor` in Task 1 - see
[`task-1-notes.md`](task-1-notes.md).

## Part A - Dapper query handler (this task)

[`Queries/GetCollectionDetailsDapperQuery.cs`](Queries/GetCollectionDetailsDapperQuery.cs):

```csharp
namespace QuotesApi.Queries;

public sealed record GetCollectionDetailsDapperQuery(Guid CollectionId);
```

[`Queries/GetCollectionDetailsDapperQueryHandler.cs`](Queries/GetCollectionDetailsDapperQueryHandler.cs):

```csharp
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

// Same read as GetCollectionDetailsQueryHandler, but bypassing AppDbContext's
// LINQ pipeline entirely: one hand-written SQL JOIN across Collections ->
// CollectionItems -> Quotes, run through Dapper on the same underlying
// ADO.NET connection EF already owns (AppDbContext.Database.GetDbConnection()),
// so both handlers share one SQLite connection/provider rather than opening a
// second one. Dapper has no equivalent of EF's nested-projection grouping, so
// the flat rows are grouped into CollectionDetailsReadModel by hand below.
public class GetCollectionDetailsDapperQueryHandler
{
    // internal (not private) so the benchmark can measure this exact
    // statement's SQL round-trip count via a raw ADO.NET connection wrapper -
    // see CollectionReadModelBenchmarkTests's Dapper variant for why.
    internal const string Sql = """
        SELECT
            c.Id AS CollectionId,
            c.Name AS CollectionName,
            q.Id AS QuoteId,
            q.Text AS QuoteText,
            q.Author AS AuthorName,
            ci.AddedAt AS AddedAtUtc
        FROM Collections c
        LEFT JOIN CollectionItems ci ON ci.CollectionId = c.Id
        LEFT JOIN Quotes q ON q.Id = ci.QuoteId
        WHERE c.Id = @CollectionId
        """;

    private readonly AppDbContext _db;

    public GetCollectionDetailsDapperQueryHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CollectionDetailsReadModel?> HandleAsync(
        GetCollectionDetailsDapperQuery query,
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();

        var rows = (await connection.QueryAsync<CollectionDetailsFlatRow>(
            new CommandDefinition(
                Sql,
                new { query.CollectionId },
                cancellationToken: cancellationToken))).AsList();

        if (rows.Count == 0)
            return null;

        // LEFT JOIN means an item-less collection comes back as one row with
        // every Quote column null - filter those out rather than emitting a
        // phantom item.
        var items = rows
            .Where(r => r.QuoteId is not null)
            .Select(r => new CollectionItemReadModel(
                (int)r.QuoteId!.Value,
                r.QuoteText!,
                r.AuthorName!,
                DateTimeOffset.Parse(r.AddedAtUtc!, CultureInfo.InvariantCulture)))
            .ToList();

        var first = rows[0];
        return new CollectionDetailsReadModel(Guid.Parse(first.CollectionId), first.CollectionName, items.Count, items);
    }

    // Dapper materializes this by setting properties off the raw ADO reader
    // values, not by matching a constructor signature - so the property
    // types have to match what Microsoft.Data.Sqlite actually hands back for
    // each column's storage class (Guid stored as TEXT comes back as
    // string, INTEGER columns come back as long), not the richer CLR types
    // the read model itself exposes. Those richer types get reconstructed by
    // hand above.
    private sealed class CollectionDetailsFlatRow
    {
        public string CollectionId { get; set; } = default!;
        public string CollectionName { get; set; } = default!;
        public long? QuoteId { get; set; }
        public string? QuoteText { get; set; }
        public string? AuthorName { get; set; }
        public string? AddedAtUtc { get; set; }
    }
}
```

Wired as `GET /api/collections/{id}/details/dapper`, side by side with the
EF endpoint, returning the identical `CollectionDetailsReadModel` shape (or
404 when the collection doesn't exist) - see
[`CollectionEndpointExtensions.cs`](Extentions/CollectionEndpointExtensions.cs).

Dapper never goes through `AppDbContext`'s LINQ pipeline for this query -
`_db` is used purely to reach the shared `DbConnection`
(`AppDbContext.Database.GetDbConnection()`), so both handlers run on the same
underlying SQLite connection/provider rather than opening a second one.

**Correctness check**
([`CollectionDetailsDapperQueryTests.cs`](../Quotes.Tests.Integration/CollectionDetailsDapperQueryTests.cs)):
for the same seeded collection, the Dapper handler's `CollectionDetailsReadModel`
matches the EF handler's field-for-field (id, name, item count, and every
item's quote id/text/author/added-at), plus the same null/empty/missing-collection
cases and the HTTP endpoint end-to-end. This is what justifies trusting the
timing numbers below - a faster handler that returned the wrong shape
wouldn't be worth measuring.

## Evidence: hitting the Dapper endpoint on localhost

Ran the app (`dotnet run`, `http://localhost:5292`), logged in as the seeded
test user, created two quotes and a collection ("Task 2 Dapper Demo"), added
both quotes to it, then hit the new endpoint directly:

- [`screenshots/collection-details-dapper-readmodel.png`](screenshots/collection-details-dapper-readmodel.png) -
  `GET /api/collections/{id}/details/dapper` live on `localhost:5292`,
  returning the same flattened `CollectionDetailsReadModel` shape as the EF
  endpoint (`collectionId`, `collectionName`, `itemCount`, and `items` with
  `quoteId`/`quoteText`/`authorName`/`addedAtUtc` inlined) - byte-for-byte
  the same JSON the EF `/details` endpoint returned for the same collection
  in the same run, confirming the two handlers agree outside of the test
  suite too.

## Part B - Timing comparison

[`CollectionReadModelBenchmarkTests.cs`](../Quotes.Tests.Integration/CollectionReadModelBenchmarkTests.cs)
extends Task 1's benchmark into a three-way comparison, same 1-warmup/5-measured-iteration
pattern, at two collection sizes: 30 items (Task 1's size) and 50 items -
the write model's own cap (`Collection.AddItem` throws `DomainException`
past 50 items, so a 200-item collection isn't reachable through the real
write path at all).

**A wrinkle on query counting.** EF's `DbCommandInterceptor` (used for the
naive and EF-read-model rows) never fires for Dapper's calls - confirmed by
a throwaway probe: running a Dapper query against the connection an
interceptor-wired `AppDbContext` was watching left `counter.Count` at 0,
because Dapper calls `CreateCommand()`/`ExecuteReader` directly on the
connection and never touches EF's `RelationalCommand` pipeline the
interceptor hooks into. The obvious fix - wrap the connection in a full EF
`AppDbContext` (`UseSqlite(proxyConnection)`) so the interceptor has
something EF-shaped to watch - was tried and measured: a single call took
over five minutes (EF's Sqlite provider does not behave well when handed a
`DbConnection` that isn't a `SqliteConnection`), which would have made the
timed loop meaningless. `CountingDbConnection` (new, in the test project)
counts SQL round trips at the raw ADO.NET level instead, with no EF
involved at all - confirmed fast (well under a millisecond) and correct.
Since the Dapper handler always issues exactly one static SQL statement
with no per-item loop, its query count can't vary by item count or
iteration, so it's measured once per benchmark run via this wrapper rather
than re-proven on every timed call - the same invariant the EF variant's
count already relies on (always 1, regardless of collection size). The
handler's SQL was made `internal` specifically so this measurement runs the
exact same statement rather than a hand-duplicated copy that could drift.

Actual run output (this machine, 2026-08-22):

```
-- Collection details, 30 items, 5 measured iterations (1 warmup discarded) --
Variant                                       Avg ms   SQL queries
------------------------------------------------------------------
Load aggregate + manual flatten (N+1)           5.00            31
EF read model (projected, single query)         0.91             1
Dapper read model (raw SQL, single query)       0.36             1

-- Collection details, 50 items, 5 measured iterations (1 warmup discarded) --
Variant                                       Avg ms   SQL queries
------------------------------------------------------------------
Load aggregate + manual flatten (N+1)          10.09            51
EF read model (projected, single query)         0.85             1
Dapper read model (raw SQL, single query)       0.35             1
```

| Items | Variant | Avg ms | SQL queries |
|---:|---|---:|---:|
| 30 | Load aggregate + manual flatten (N+1) | 5.00 | 31 |
| 30 | EF read model | 0.91 | 1 |
| 30 | Dapper read model | 0.36 | 1 |
| 50 | Load aggregate + manual flatten (N+1) | 10.09 | 51 |
| 50 | EF read model | 0.85 | 1 |
| 50 | Dapper read model | 0.35 | 1 |

The real signal here isn't naive-vs-anything (that gap already scales
linearly with N and was Task 1's finding) - it's EF vs Dapper. Dapper beat
EF by roughly 2.4-2.7x at both sizes (0.36ms vs 0.91ms at 30 items, 0.35ms
vs 0.85ms at 50 items), but the *absolute* gap barely moved - about
0.5-0.6ms either way - while going from 30 to 50 items. That's consistent
with both being a single query, single round trip: the difference is
per-call overhead (EF's LINQ-to-SQL translation and change-tracker-aware
materialization vs Dapper's leaner reflection-based row mapping), not
something that compounds with row count in this range. Query count is
identical (1) for both at both sizes - Dapper's win here is 100% about
per-call CPU overhead, not fewer round trips.

## The rule

For this collection-details read, the ratio (Dapper ~2.5x faster than EF)
looks dramatic in isolation, but the *absolute* numbers - sub-millisecond
for both, at the largest collection this domain allows - mean the switch
buys about half a millisecond per request, which is noise next to network
latency, JSON serialization, or auth middleware in any real deployment.
Don't drop to Dapper here just because the ratio is favorable: reach for it
only when a read sits on a genuinely hot path (called at high frequency, or
already identified as the bottleneck by profiling under real load) *and*
the EF projection's own overhead - not the number of round trips, which EF
can already get to 1 with a proper projection - is the measured cost, not a
guess. If Task 1's finding was "the query count is what kills you, and EF's
projection already fixes that," this task's finding is "once you're down to
one query, EF's remaining overhead over raw ADO.NET is real but small, and
worth paying for the mapping, tracking, and LINQ-composability EF gives you
unless a specific measured hot path says otherwise." Keep EF as the
default; earn the switch to Dapper with a number from this exact endpoint
under this exact load, not with the folklore that "Dapper is faster."

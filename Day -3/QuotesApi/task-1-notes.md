# Day 12, Task 1: Read Models + CQRS-lite (no event sourcing)

Split the `Collection` feature into two completely separate roads: a write path
(command, validated, goes through the aggregate) and a read path (query,
denormalized, flat, shaped for the "collection details" screen). No event
sourcing - just two independent code paths against the same tables.

Prerequisite fix (not part of the CQRS split itself, but blocking it): the
existing `Collection` aggregate (from `day-1-task7-collection-aggregate`) had
`CollectionItem.QuoteId` typed as `Guid` while `Quote.Id` is `int` - there was
no real foreign key between the two tables, which meant no genuine single-SQL
join was possible for the read model. Changed `QuoteId` to `int` and added a
DB-level FK (`CollectionItems.QuoteId -> Quotes.Id`) via a navigation-less
`HasOne<Quote>()` relationship in `AppDbContext`, so `Collection` still never
holds a `Quote` reference - only an id - while the database now enforces that
the id is real.

## Part A - Write model

[`Commands/AddQuoteToCollectionCommand.cs`](Commands/AddQuoteToCollectionCommand.cs):

```csharp
namespace QuotesApi.Commands;

public sealed record AddQuoteToCollectionCommand(Guid CollectionId, int QuoteId);
```

[`Commands/AddQuoteToCollectionCommandHandler.cs`](Commands/AddQuoteToCollectionCommandHandler.cs):

```csharp
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Commands;

// The write side of the Collection feature. This handler only ever deals in
// the normalized aggregate shape (Collection -> CollectionItems, no quote
// text/author) - it loads the tracked aggregate, delegates every invariant
// check to Collection.AddItem, and persists. It has no idea a "collection
// details" screen exists; that's the read model's problem, not this one's.
public class AddQuoteToCollectionCommandHandler
{
    private readonly ICollectionRepository _collectionRepository;
    private readonly IClock _clock;

    public AddQuoteToCollectionCommandHandler(
        ICollectionRepository collectionRepository,
        IClock clock)
    {
        _collectionRepository = collectionRepository;
        _clock = clock;
    }

    // Returns null when the collection doesn't exist, so callers (endpoints)
    // decide how that maps to a response, same as ICollectionRepository's own
    // GetByIdAsync. DomainException from AddItem is left to bubble up to
    // ExceptionMiddleware - this handler doesn't catch or reshape it.
    public async Task<Collection?> HandleAsync(
        AddQuoteToCollectionCommand command,
        CancellationToken cancellationToken)
    {
        var collection = await _collectionRepository.GetByIdAsync(
            command.CollectionId,
            cancellationToken);

        if (collection is null)
            return null;

        collection.AddItem(command.QuoteId, _clock);

        await _collectionRepository.UpdateAsync(collection, cancellationToken);

        return collection;
    }
}
```

The `POST /api/collections/{id}/items` endpoint now only does authorization
and translates the command result into a response - all invariant enforcement
lives in `Collection.AddItem`, invoked exclusively through this handler.

## Part B - Read model

[`Queries/CollectionDetailsReadModel.cs`](Queries/CollectionDetailsReadModel.cs):

```csharp
namespace QuotesApi.Queries;

// Denormalized on purpose: shaped exactly for a "collection details" screen,
// with quote text/author pulled in directly. No caller of this type ever
// needs to walk Collection -> CollectionItem -> Quote to render a screen.
//
// Tags aren't included here: unlike Author (already a plain string field on
// Quote), this codebase has no Tags concept on Quote at all, so there's
// nothing real to project - adding a field that would always be an empty
// list would just be a fabricated placeholder.
public sealed record CollectionDetailsReadModel(
    Guid CollectionId,
    string CollectionName,
    int ItemCount,
    List<CollectionItemReadModel> Items);

public sealed record CollectionItemReadModel(
    int QuoteId,
    string QuoteText,
    string AuthorName,
    DateTimeOffset AddedAtUtc);
```

[`Queries/GetCollectionDetailsQuery.cs`](Queries/GetCollectionDetailsQuery.cs):

```csharp
namespace QuotesApi.Queries;

public sealed record GetCollectionDetailsQuery(Guid CollectionId);
```

[`Queries/GetCollectionDetailsQueryHandler.cs`](Queries/GetCollectionDetailsQueryHandler.cs):

```csharp
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

// The read side of the Collection feature. This handler never touches
// ICollectionRepository or the Collection aggregate - it goes straight at
// AppDbContext with a single AsNoTracking() projection that joins
// Collections -> CollectionItems -> Quotes and flattens the result directly
// into CollectionDetailsReadModel. No domain entities are hydrated, no
// invariants are checked (there's nothing to validate on a read), and the
// aggregate's encapsulation is irrelevant here - the query just asks the
// database for the exact shape a "collection details" screen needs.
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
        // A single query: EF Core translates this into one SQL statement
        // (LEFT JOIN CollectionItems, LEFT JOIN Quotes) and buffers the rows
        // client-side into the nested Items list - there is no per-item
        // round trip and nothing here is a tracked domain entity.
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

Wired as `GET /api/collections/{id}/details`, returning `CollectionDetailsReadModel`
directly (or 404 when the collection doesn't exist).

Confirmed in [`CollectionDetailsQueryTests.cs`](../Quotes.Tests.Integration/CollectionDetailsQueryTests.cs)
via a `DbCommandInterceptor`-based counter: the handler issues exactly **1** SQL
statement and leaves `ChangeTracker.Entries()` empty, for both a populated and
an empty (`Items.Count == 0`) collection.

## Evidence: hitting both endpoints on localhost

Ran the app (`dotnet run`, `http://localhost:5292`), logged in as the seeded
test user, created two quotes and a collection, added both quotes to it, then
hit both endpoints directly in a browser for the same collection id:

- [`screenshots/collection-aggregate-writemodel.png`](screenshots/collection-aggregate-writemodel.png) -
  `GET /api/collections/{id}` (the write side's own read-back): normalized,
  `items` only carries `quoteId`/`addedAt` - no quote text or author.
- [`screenshots/collection-details-readmodel.png`](screenshots/collection-details-readmodel.png) -
  `GET /api/collections/{id}/details` (the read model): denormalized,
  `items` carries `quoteText`/`authorName` inlined, plus the flat
  `collectionName`/`itemCount` a details screen actually wants.

Same collection, same two items, two genuinely different shapes - proof the
write and read paths are not just the same data serialized twice.

## What got simpler

The display endpoint no longer needs to know about aggregate invariants,
`ICollectionRepository`, or how to navigate `Collection -> CollectionItem ->
Quote` - it just asks `GetCollectionDetailsQueryHandler` for the exact shape
it wants and gets back a flat DTO with quote text and author already filled
in, in one round trip.

## Part C - Proof they're separate

[`CollectionReadModelBenchmarkTests.cs`](../Quotes.Tests.Integration/CollectionReadModelBenchmarkTests.cs)
seeds one collection with 30 items and times two ways of producing a
`CollectionDetailsReadModel` for it, each averaged over 5 measured iterations
(1 warmup discarded, same pattern as Day 10's `AsNoTracking()` benchmark),
with SQL round trips counted via the same `DbCommandInterceptor` used in
Part B's tests rather than trusting timing alone:

1. **Load aggregate + manual flatten** - what a caller stuck with only the
   write side would have to do: `db.Collections.FirstAsync(...)` to load the
   tracked `Collection`, then one `db.Quotes.FirstAsync(...)` per item to
   resolve its text/author, since `Collection` only ever carries `QuoteId`s.
2. **Read model** - a single call to `GetCollectionDetailsQueryHandler.HandleAsync`.

Actual run output (this machine, 2026-08-22):

```
-- Collection details, 30 items, 5 measured iterations (1 warmup discarded) --
Variant                                       Avg ms   SQL queries
------------------------------------------------------------------
Load aggregate + manual flatten (N+1)          20.79            31
Read model (projected, single query)            3.24             1
```

| Variant | Avg ms | SQL queries |
|---|---:|---:|
| Load aggregate + manual flatten (N+1) | 20.79 | 31 |
| Read model (projected, single query) | 3.24 | 1 |

**6.4x faster, 31x fewer round trips.** The read model's query count is fixed
at 1 regardless of item count; the naive path is `1 + itemCount` round trips,
so the gap widens as collections grow - at 30 items it's already a 31x
difference, and it scales linearly from there.

Also added:

- [`AddQuoteToCollectionCommandHandlerTests.cs`](../Quotes.Tests.Unit/AddQuoteToCollectionCommandHandlerTests.cs) -
  unit tests for the command handler: happy path persists via the repository;
  a missing collection returns `null` without calling `UpdateAsync`; both of
  `Collection.AddItem`'s invariants (duplicate `QuoteId`, 51st item) still
  throw `DomainException` through the handler and leave `UpdateAsync`
  uncalled.
- [`CollectionDetailsQueryTests.cs`](../Quotes.Tests.Integration/CollectionDetailsQueryTests.cs) -
  tests for the query handler: correct flattened shape for a populated and an
  empty collection, `null` for a non-existent collection, an empty
  `ChangeTracker` (no tracking), and exactly 1 SQL statement (no N+1), plus
  the `GET /api/collections/{id}/details` endpoint end-to-end.

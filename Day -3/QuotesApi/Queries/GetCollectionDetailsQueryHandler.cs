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

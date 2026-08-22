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
    private const string Sql = """
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

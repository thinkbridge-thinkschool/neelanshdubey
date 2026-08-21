using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class AuthorEndpointExtensions
{
    /// <summary>
    /// GET /api/authors/with-quotes is a deliberate N+1: it loads every
    /// author with a plain ToList(), then re-queries Quotes once per author
    /// inside the loop instead of using Include/split query. This is the
    /// Day 11 Task 1 profiling target -- 1 query for authors + N queries
    /// for quotes (200 extra roundtrips), each hitting an unindexed
    /// Quote.AuthorId column.
    /// </summary>
    public static WebApplication MapAuthorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/authors/with-quotes", (AppDbContext db) =>
        {
            var authors = db.Authors.ToList();

            var result = new List<AuthorWithQuotesDto>(authors.Count);

            foreach (var author in authors)
            {
                var quotes = db.Quotes
                    .Where(q => q.AuthorId == author.Id)
                    .ToList();

                result.Add(new AuthorWithQuotesDto(
                    author.Id,
                    author.Name,
                    quotes
                        .Select(q => new QuoteSummaryDto(q.Id, q.Text, q.CreatedAt))
                        .ToList()));
            }

            return Results.Ok(result);
        });

        return app;
    }
}

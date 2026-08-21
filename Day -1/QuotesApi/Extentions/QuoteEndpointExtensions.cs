using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static WebApplication MapQuoteEndpoints(
        this WebApplication app)
    {
        app.MapGet("/api/quotes", async (
            IQuoteRepository repository,
            CancellationToken cancellationToken,
            int? page,
            int? size) =>
        {
            var currentPage = page ?? 1;
            var currentSize = size ?? 10;

            if (currentPage < 1)
                currentPage = 1;

            if (currentSize < 1 || currentSize > 100)
                currentSize = 10;

            var quotes = await repository.GetAllAsync(
                currentPage,
                currentSize,
                cancellationToken);

            return Results.Ok(quotes);
        });

        app.MapPost("/api/quotes", async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            IQuoteValidator validator,
            CancellationToken cancellationToken) =>
        {
            var errors = validator.Validate(
                request.AuthorId,
                request.Text);

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var quote = Quote.Create(
                request.AuthorId,
                request.Text);

            var created = await repository.AddAsync(
                quote,
                cancellationToken);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        }).RequireAuthorization();

        app.MapGet("/api/quotes/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        app.MapDelete("/api/quotes/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireAuthorization();

        return app;
    }
}
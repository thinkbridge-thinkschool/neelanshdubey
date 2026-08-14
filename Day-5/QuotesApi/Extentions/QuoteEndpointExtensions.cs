using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
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
            AppDbContext db,
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

            var ownerIds = quotes
                .Select(q => q.OwnerId)
                .Distinct()
                .ToList();

            var ownersById = await db.Users
                .AsNoTracking()
                .Where(u => ownerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, cancellationToken);

            var enriched = quotes.Select(quote => new
            {
                quote.Id,
                quote.Author,
                quote.Text,
                quote.CreatedAt,
                quote.OwnerId,
                OwnerEmail = ownersById.GetValueOrDefault(quote.OwnerId)?.Email
            });

            return Results.Ok(enriched);
        });

        app.MapPost("/api/quotes", async (
            CreateQuoteRequest request,
            HttpContext httpContext,
            IQuoteRepository repository,
            IQuoteValidator validator,
            CancellationToken cancellationToken) =>
        {
            var errors = validator.Validate(
                request.Author,
                request.Text);

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var ownerId = httpContext.User.GetUserId();

            if (ownerId is null)
            {
                return Results.Unauthorized();
            }

            var quote = Quote.Create(
                request.Author,
                request.Text,
                ownerId.Value);

            var created = await repository.AddAsync(
                quote,
                cancellationToken);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        }).RequireAuthorization();

        app.MapPut("/api/quotes/{id:int}", async (
            int id,
            UpdateQuoteRequest request,
            HttpContext httpContext,
            IQuoteRepository repository,
            IQuoteValidator validator,
            IAuthorizationService authorizationService,
            CancellationToken cancellationToken) =>
        {
            var errors = validator.Validate(
                request.Author,
                request.Text);

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            if (quote is null)
            {
                return Results.NotFound();
            }

            // The ownership rule is identical for edit and delete, so this
            // reuses the "can-delete-own-quote" resource-based policy rather
            // than duplicating a same-owner check under a second policy name.
            var authResult = await authorizationService.AuthorizeAsync(
                httpContext.User,
                quote,
                "can-delete-own-quote");

            if (!authResult.Succeeded)
            {
                return Results.Forbid();
            }

            var updated = await repository.UpdateAsync(
                id,
                request.Author,
                request.Text,
                cancellationToken);

            return updated is null
                ? Results.NotFound()
                : Results.Ok(updated);
        }).RequireAuthorization("can-edit-quotes");

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
            HttpContext httpContext,
            IQuoteRepository repository,
            IAuthorizationService authorizationService,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            if (quote is null)
            {
                return Results.NotFound();
            }

            var authResult = await authorizationService.AuthorizeAsync(
                httpContext.User,
                quote,
                "can-delete-own-quote");

            if (!authResult.Succeeded)
            {
                return Results.Forbid();
            }

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
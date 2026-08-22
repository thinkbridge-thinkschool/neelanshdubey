using Microsoft.AspNetCore.Authorization;
using QuotesApi.Commands;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static WebApplication MapCollectionEndpoints(
        this WebApplication app)
    {
        app.MapPost("/api/collections", async (
            CreateCollectionRequest request,
            HttpContext httpContext,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var ownerId = httpContext.User.GetUserId();

            if (ownerId is null)
            {
                return Results.Unauthorized();
            }

            // Collection's constructor throws DomainException on an invalid
            // Name; ExceptionMiddleware maps that to a 400 ProblemDetails.
            var collection = new Collection(request.Name, ownerId.Value);

            var created = await repository.AddAsync(
                collection,
                cancellationToken);

            return Results.Created(
                $"/api/collections/{created.Id}",
                created);
        }).RequireAuthorization();

        app.MapGet("/api/collections/{id:guid}", async (
            Guid id,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return collection is null
                ? Results.NotFound()
                : Results.Ok(collection);
        });

        app.MapPost("/api/collections/{id:guid}/items", async (
            Guid id,
            AddCollectionItemRequest request,
            HttpContext httpContext,
            ICollectionRepository repository,
            IAuthorizationService authorizationService,
            AddQuoteToCollectionCommandHandler handler,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(
                id,
                cancellationToken);

            if (collection is null)
            {
                return Results.NotFound();
            }

            var authResult = await authorizationService.AuthorizeAsync(
                httpContext.User,
                collection,
                "can-manage-own-collection");

            if (!authResult.Succeeded)
            {
                return Results.Forbid();
            }

            // The command handler owns loading the aggregate, enforcing
            // AddItem's invariants, and persisting - the endpoint's only job
            // is auth plus translating the command result into a response.
            // This means the aggregate is fetched twice per request (once
            // here so AuthorizeAsync has a Collection resource to check
            // ownership against, once inside the handler): a deliberate
            // trade-off so the handler stays independently usable/testable
            // without ever assuming a pre-loaded, already-authorized
            // aggregate was handed to it.
            var updated = await handler.HandleAsync(
                new AddQuoteToCollectionCommand(id, request.QuoteId),
                cancellationToken);

            return Results.Ok(updated);
        }).RequireAuthorization();

        app.MapDelete("/api/collections/{id:guid}/items/{quoteId:int}", async (
            Guid id,
            int quoteId,
            HttpContext httpContext,
            ICollectionRepository repository,
            IAuthorizationService authorizationService,
            CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(
                id,
                cancellationToken);

            if (collection is null)
            {
                return Results.NotFound();
            }

            var authResult = await authorizationService.AuthorizeAsync(
                httpContext.User,
                collection,
                "can-manage-own-collection");

            if (!authResult.Succeeded)
            {
                return Results.Forbid();
            }

            // RemoveItem throws DomainException if quoteId isn't in the
            // collection; ExceptionMiddleware maps that to 400.
            collection.RemoveItem(quoteId);

            await repository.UpdateAsync(collection, cancellationToken);

            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }
}

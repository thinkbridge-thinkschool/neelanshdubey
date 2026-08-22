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

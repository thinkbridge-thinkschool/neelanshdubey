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

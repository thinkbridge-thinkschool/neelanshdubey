namespace QuotesApi.Commands;

public sealed record AddQuoteToCollectionCommand(Guid CollectionId, int QuoteId);

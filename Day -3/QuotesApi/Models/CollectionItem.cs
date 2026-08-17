namespace QuotesApi.Models;

public sealed record CollectionItem(Guid QuoteId, DateTimeOffset AddedAt);

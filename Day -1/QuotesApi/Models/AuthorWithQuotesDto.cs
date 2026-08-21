namespace QuotesApi.Models;

public record QuoteSummaryDto(
    int Id,
    string Text,
    DateTimeOffset CreatedAt);

public record AuthorWithQuotesDto(
    int AuthorId,
    string Name,
    List<QuoteSummaryDto> Quotes);

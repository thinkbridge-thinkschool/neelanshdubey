namespace QuotesApi.Models;

public record CreateQuoteRequest(
    int AuthorId,
    string Text);

namespace QuotesApi.Models;

public record UpdateQuoteRequest(
    string Author,
    string Text);

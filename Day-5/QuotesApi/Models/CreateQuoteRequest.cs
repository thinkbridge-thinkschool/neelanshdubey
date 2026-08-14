namespace QuotesApi.Models;

public record CreateQuoteRequest(
    string Author,
    string Text);
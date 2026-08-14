namespace QuotesApi.Services;

public class QuoteValidator : IQuoteValidator
{
    public Dictionary<string, string[]> Validate(
        string author,
        string text)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(author))
        {
            errors["author"] = ["Author is required."];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            errors["text"] = ["Text is required."];
        }

        return errors;
    }
}
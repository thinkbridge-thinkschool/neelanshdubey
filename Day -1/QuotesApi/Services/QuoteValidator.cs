namespace QuotesApi.Services;

public class QuoteValidator : IQuoteValidator
{
    public Dictionary<string, string[]> Validate(
        int authorId,
        string text)
    {
        var errors = new Dictionary<string, string[]>();

        if (authorId <= 0)
        {
            errors["authorId"] = ["AuthorId is required."];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            errors["text"] = ["Text is required."];
        }

        return errors;
    }
}

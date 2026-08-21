namespace QuotesApi.Services;

public interface IQuoteValidator
{
    Dictionary<string, string[]> Validate(
        int authorId,
        string text);
}

namespace QuotesApi.Services;

public interface IQuoteValidator
{
    Dictionary<string, string[]> Validate(
        string author,
        string text);
}
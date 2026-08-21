namespace QuotesApi.Models;

public class Author
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<Quote> Quotes { get; set; } = [];
}

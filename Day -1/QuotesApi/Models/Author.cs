using System.Text.Json.Serialization;

namespace QuotesApi.Models;

public class Author
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // EF Core fixes up this collection from the inverse side whenever a
    // query also materializes this author's Quotes (e.g. Include(Author)
    // from the Quotes side), which would otherwise create a Quote -> Author
    // -> Quotes -> Author serialization cycle.
    [JsonIgnore]
    public List<Quote> Quotes { get; set; } = [];
}

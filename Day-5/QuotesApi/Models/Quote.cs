namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsDeleted { get; private set; }

    public int OwnerId { get; set; }

    public static Quote Create(string author, string text, int ownerId)
    {
        var (validAuthor, validText) = ValidateFields(author, text);

        return new Quote
        {
            Author = validAuthor,
            Text = validText,
            OwnerId = ownerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(string author, string text)
    {
        var (validAuthor, validText) = ValidateFields(author, text);

        Author = validAuthor;
        Text = validText;
    }

    public void SoftDelete() => IsDeleted = true;

    private static (string Author, string Text) ValidateFields(string? author, string? text)
    {
        var trimmedAuthor = author?.Trim() ?? string.Empty;
        var trimmedText = text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedAuthor))
            throw new DomainException("Author is required.");

        if (trimmedAuthor.Length > 200)
            throw new DomainException("Author must be 200 characters or fewer.");

        if (string.IsNullOrWhiteSpace(trimmedText))
            throw new DomainException("Text is required.");

        if (trimmedText.Length > 1000)
            throw new DomainException("Text must be 1000 characters or fewer.");

        return (trimmedAuthor, trimmedText);
    }
}
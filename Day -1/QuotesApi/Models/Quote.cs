namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; set; }

    public int AuthorId { get; set; }

    public Author Author { get; set; } = null!;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsDeleted { get; private set; }

    public static Quote Create(int authorId, string text)
    {
        var trimmedText = text?.Trim() ?? string.Empty;

        if (authorId <= 0)
            throw new DomainException("AuthorId is required.");

        if (string.IsNullOrWhiteSpace(trimmedText))
            throw new DomainException("Text is required.");

        if (trimmedText.Length > 1000)
            throw new DomainException("Text must be 1000 characters or fewer.");

        return new Quote
        {
            AuthorId = authorId,
            Text = trimmedText,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void SoftDelete() => IsDeleted = true;
}

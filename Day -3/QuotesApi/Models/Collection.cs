using QuotesApi.Services;

namespace QuotesApi.Models;

public class Collection
{
    private const int MaxItems = 50;

    private readonly List<CollectionItem> _items = [];

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int OwnerId { get; private set; }

    public IReadOnlyList<CollectionItem> Items => _items;

    // For EF Core materialization only.
    private Collection()
    {
    }

    public Collection(string name, int ownerId)
    {
        Id = Guid.NewGuid();
        Name = ValidateName(name);
        OwnerId = ownerId;
    }

    public void AddItem(int quoteId, IClock clock)
    {
        if (_items.Count >= MaxItems)
            throw new DomainException($"A collection cannot contain more than {MaxItems} items.");

        if (_items.Any(i => i.QuoteId == quoteId))
            throw new DomainException("This quote is already in the collection.");

        _items.Add(new CollectionItem(quoteId, clock.UtcNow));
    }

    // Throws rather than no-ops: removing something that was never there is
    // treated as caller error, consistent with AddItem's other invariants
    // failing loudly instead of silently doing nothing.
    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);

        if (item is null)
            throw new DomainException("This quote is not in the collection.");

        _items.Remove(item);
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
            throw new DomainException("Name is required.");

        if (trimmed.Length < 3)
            throw new DomainException("Name must be at least 3 characters.");

        if (trimmed.Length > 80)
            throw new DomainException("Name must be 80 characters or fewer.");

        return trimmed;
    }
}

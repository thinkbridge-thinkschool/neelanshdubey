using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<CollectionRepository> _logger;

    public CollectionRepository(
        AppDbContext db,
        ILogger<CollectionRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Deliberately tracked (unlike QuoteRepository.GetByIdAsync's
    // AsNoTracking): callers mutate the returned aggregate via
    // AddItem/RemoveItem and then call UpdateAsync on the same scoped
    // DbContext, so EF needs to already be tracking it to pick up the
    // owned-collection change on SaveChangesAsync.
    public async Task<Collection?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await _db.Collections
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Collection> AddAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created collection with ID {CollectionId}",
            collection.Id);

        return collection;
    }

    public async Task UpdateAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated collection with ID {CollectionId}",
            collection.Id);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var collection = await _db.Collections
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (collection is null)
            return false;

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted collection with ID {CollectionId}",
            id);

        return true;
    }
}

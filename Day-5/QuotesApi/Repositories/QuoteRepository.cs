using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(
        AppDbContext db,
        ILogger<QuoteRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<Quote>> GetAllAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created quote with ID {QuoteId}",
            quote.Id);

        return quote;
    }

    public async Task<Quote?> UpdateAsync(
        int id,
        string author,
        string text,
        CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

        if (quote is null)
            return null;

        quote.Update(author, text);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated quote with ID {QuoteId}",
            id);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

        if (quote is null)
            return false;

        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted quote with ID {QuoteId}",
            id);

        return true;
    }
}
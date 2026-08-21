using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>()
            .HasOne(q => q.Author)
            .WithMany(a => a.Quotes)
            .HasForeignKey(q => q.AuthorId)
            .IsRequired();

        // EF Core adds an index on Quote.AuthorId by convention (it's a required
        // FK). The DropQuoteAuthorIdIndex migration removes it from the actual
        // database on purpose: Day 11 Task 1 profiles this query path with no
        // index present, so the model here still reflects the FK-convention
        // index while the applied schema does not.
    }
}

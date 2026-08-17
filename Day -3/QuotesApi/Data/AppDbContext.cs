using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Collection> Collections => Set<Collection>();

    // Quote/User/RefreshToken rely on EF conventions alone; Collection needs
    // this override because OwnsMany (and its shadow/composite key) has no
    // convention-based equivalent.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Collection>(collection =>
        {
            collection.HasKey(c => c.Id);

            collection.OwnsMany(c => c.Items, items =>
            {
                items.ToTable("CollectionItems");
                items.WithOwner().HasForeignKey("CollectionId");
                items.Property<Guid>("CollectionId");
                items.HasKey("CollectionId", nameof(CollectionItem.QuoteId));
            });

            collection.Navigation(c => c.Items)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
using Microsoft.EntityFrameworkCore;

namespace NPlusOneFix;

public class AppDbContext : DbContext
{
    private readonly string _connectionString;
    private readonly QueryCounterInterceptor? _interceptor;

    public AppDbContext(string connectionString, QueryCounterInterceptor? interceptor = null)
    {
        _connectionString = connectionString;
        _interceptor = interceptor;
    }

    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(_connectionString).UseLazyLoadingProxies();

        if (_interceptor is not null)
        {
            optionsBuilder.AddInterceptors(_interceptor);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>()
            .HasOne(b => b.Author)
            .WithMany(a => a.Books)
            .HasForeignKey(b => b.AuthorId);

        // Covers the projection query's SELECT list (Title, PublishedYear) so the
        // engine can satisfy it from the index alone - no Key Lookup into PK_Books.
        modelBuilder.Entity<Book>()
            .HasIndex(b => b.AuthorId)
            .IncludeProperties(b => new { b.PublishedYear, b.Title });
    }
}

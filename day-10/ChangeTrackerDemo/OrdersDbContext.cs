using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ChangeTrackerDemo;

// sqlLogger, when supplied, receives one call per "Executed DbCommand" line EF Core
// logs - this is how Part A proves whether a given query actually hit the database.
public class OrdersDbContext(string connectionString, Action<string>? sqlLogger = null) : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(connectionString);

        if (sqlLogger is not null)
        {
            optionsBuilder.LogTo(sqlLogger, [RelationalEventId.CommandExecuted], LogLevel.Information);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.OrderId);
            entity.Property(o => o.Status).HasMaxLength(20);
            entity.Property(o => o.Amount).HasColumnType("decimal(10,2)");
        });
    }
}

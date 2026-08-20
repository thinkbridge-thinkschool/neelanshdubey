using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

// Reuses the Day 9 SQL Server container (docker: day9-sql, host port 1434) rather than
// starting a new one. The Day 8 "Day8Indexing" database (dbo.Orders: OrderId, CustomerId,
// OrderDate, Status, Amount, Notes) is not currently running - that container has been
// stopped since Day 8 wrapped up - so this project seeds a fresh database with the same
// core Orders schema (minus the filler Notes column, which isn't needed for this exercise).
public static class DbInitializer
{
    public const string ConnectionString =
        "Server=localhost,1434;Database=Day10ChangeTracking;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

    public const int TargetRowCount = 10_000;

    public static void EnsureSeeded()
    {
        using var context = new OrdersDbContext(ConnectionString);
        context.Database.EnsureCreated();

        var count = context.Orders.Count();
        if (count == TargetRowCount)
        {
            Console.WriteLine($"Orders table already has exactly {TargetRowCount:N0} rows - skipping seed.");
            return;
        }

        if (count != 0)
        {
            Console.WriteLine($"Orders table has {count:N0} rows (expected {TargetRowCount:N0}) - truncating and reseeding.");
            context.Database.ExecuteSqlRaw("TRUNCATE TABLE dbo.Orders; DBCC CHECKIDENT ('dbo.Orders', RESEED, 0);");
        }

        Console.WriteLine($"Seeding {TargetRowCount:N0} Orders rows...");
        context.Database.ExecuteSqlRaw(SeedSql);

        var finalCount = context.Orders.Count();
        Console.WriteLine($"Seed complete. Row count = {finalCount:N0}.");
    }

    // Set-based generation via a tally CTE (same technique as Day-8/task1.sql), capped
    // at exactly TargetRowCount rows via TOP.
    private const string SeedSql = """
        ;WITH
        E1(N) AS (SELECT N FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(N)),
        E2(N) AS (SELECT 1 FROM E1 a CROSS JOIN E1 b),
        E3(N) AS (SELECT 1 FROM E2 a CROSS JOIN E2 b),
        E4(N) AS (SELECT 1 FROM E3 a CROSS JOIN E2 b),
        Tally AS (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N FROM E4)
        INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount)
        SELECT TOP (10000)
            CustomerId = ABS(CHECKSUM(NEWID())) % 500 + 1,
            OrderDate  = DATEADD(SECOND, -(ABS(CHECKSUM(NEWID())) % (730 * 24 * 3600)), SYSDATETIME()),
            Status     = CASE ABS(CHECKSUM(NEWID())) % 4
                             WHEN 0 THEN 'Pending'
                             WHEN 1 THEN 'Shipped'
                             WHEN 2 THEN 'Delivered'
                             ELSE 'Cancelled'
                         END,
            Amount     = CAST(ABS(CHECKSUM(NEWID())) % 100000 / 100.0 AS DECIMAL(10,2))
        FROM Tally;
        """;
}

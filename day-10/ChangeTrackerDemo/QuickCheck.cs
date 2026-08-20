using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class QuickCheck
{
    public static void Run()
    {
        Console.WriteLine("=== Quick Check: Tracked vs Untracked ===");

        using var ctx = new OrdersDbContext(DbInitializer.ConnectionString);
        var tracked = ctx.Orders.ToList();
        Console.WriteLine($"Tracked entries: {ctx.ChangeTracker.Entries().Count()}");

        using var ctx2 = new OrdersDbContext(DbInitializer.ConnectionString);
        var untracked = ctx2.Orders.AsNoTracking().ToList();
        Console.WriteLine($"Untracked entries: {ctx2.ChangeTracker.Entries().Count()}");
    }
}

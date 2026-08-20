using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class PartB_TrackedVsUntracked
{
    public static void Run(int trackedMutateId, int noTrackMutateId)
    {
        Console.WriteLine("=== Part B: Tracked vs Untracked ===");

        using (var trackedContext = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var trackedList = trackedContext.Orders.ToList();
            Console.WriteLine($"Tracked .ToList(): rows={trackedList.Count}, " +
                               $"ChangeTracker.Entries().Count()={trackedContext.ChangeTracker.Entries().Count()}");
        }

        using (var noTrackContext = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var noTrackList = noTrackContext.Orders.AsNoTracking().ToList();
            Console.WriteLine($"AsNoTracking().ToList(): rows={noTrackList.Count}, " +
                               $"ChangeTracker.Entries().Count()={noTrackContext.ChangeTracker.Entries().Count()}");
        }

        Console.WriteLine();
        Console.WriteLine("-- Mutation scenario 1: tracked entity, no explicit Update() call --");
        var trackedNewStatus = $"Tracked-{Guid.NewGuid():N}"[..20];
        using (var ctx = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var order = ctx.Orders.First(o => o.OrderId == trackedMutateId);
            order.Status = trackedNewStatus;
            var rows = ctx.SaveChanges();
            Console.WriteLine($"   SaveChanges() affected {rows} row(s) with no explicit Update() call.");
        }
        using (var verify = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var reloaded = verify.Orders.AsNoTracking().First(o => o.OrderId == trackedMutateId);
            Console.WriteLine($"   Verify (fresh context): Status='{reloaded.Status}' -> " +
                               $"persisted={reloaded.Status == trackedNewStatus} (expected true)");
        }

        Console.WriteLine();
        Console.WriteLine("-- Mutation scenario 2: AsNoTracking entity, mutate + SaveChanges (no reattach) --");
        const string attemptedStatus = "ShouldNotPersist";
        using (var ctx = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var order = ctx.Orders.AsNoTracking().First(o => o.OrderId == noTrackMutateId);
            order.Status = attemptedStatus;
            var rows = ctx.SaveChanges();
            Console.WriteLine($"   SaveChanges() affected {rows} row(s) (entity was never tracked, so there's nothing to save).");
        }
        using (var verify = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var reloaded = verify.Orders.AsNoTracking().First(o => o.OrderId == noTrackMutateId);
            Console.WriteLine($"   Verify (fresh context): Status='{reloaded.Status}' -> " +
                               $"persisted={reloaded.Status == attemptedStatus} (expected false)");
        }

        Console.WriteLine();
        Console.WriteLine("-- Mutation scenario 3: same AsNoTracking entity, this time reattached via Update() --");
        using (var ctx = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var order = ctx.Orders.AsNoTracking().First(o => o.OrderId == noTrackMutateId);
            order.Status = attemptedStatus;
            ctx.Orders.Update(order); // reattaches the detached entity and marks it Modified
            var rows = ctx.SaveChanges();
            Console.WriteLine($"   SaveChanges() affected {rows} row(s) after explicit Update() reattach.");
        }
        using (var verify = new OrdersDbContext(DbInitializer.ConnectionString))
        {
            var reloaded = verify.Orders.AsNoTracking().First(o => o.OrderId == noTrackMutateId);
            Console.WriteLine($"   Verify (fresh context): Status='{reloaded.Status}' -> " +
                               $"persisted={reloaded.Status == attemptedStatus} (expected true now)");
        }

        Console.WriteLine();
    }
}

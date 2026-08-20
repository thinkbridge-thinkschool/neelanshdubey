namespace ChangeTrackerDemo;

public static class PartA_IdentityResolution
{
    public static void Run(int targetId)
    {
        Console.WriteLine("=== Part A: Identity Resolution ===");

        var sqlRoundTrips = new List<string>();
        void LogSql(string message)
        {
            if (message.Contains("Executed DbCommand"))
            {
                sqlRoundTrips.Add(message);
            }
        }

        Order? order1a, order1b, queryX1, queryX2;

        using (var context1 = new OrdersDbContext(DbInitializer.ConnectionString, LogSql))
        {
            Console.WriteLine($"-- context1.Orders.Find({targetId}) (1st call) --");
            order1a = context1.Orders.Find(targetId);
            Console.WriteLine($"   SQL round trips so far: {sqlRoundTrips.Count}");

            Console.WriteLine($"-- context1.Orders.Find({targetId}) (2nd call, SAME context) --");
            order1b = context1.Orders.Find(targetId);
            Console.WriteLine($"   SQL round trips so far: {sqlRoundTrips.Count} " +
                               (sqlRoundTrips.Count == 1 ? "(no new query - identity map served it from memory)" : ""));

            Console.WriteLine($"   ReferenceEquals(order1a, order1b) = {ReferenceEquals(order1a, order1b)}");

            Console.WriteLine();
            Console.WriteLine("-- Contrast: an explicit LINQ query for the SAME PK always round-trips --");
            sqlRoundTrips.Clear();
            queryX1 = context1.Orders.First(o => o.OrderId == targetId);
            var tripsAfterFirst = sqlRoundTrips.Count;
            queryX2 = context1.Orders.First(o => o.OrderId == targetId);
            var tripsAfterSecond = sqlRoundTrips.Count;
            Console.WriteLine($"   SQL round trips: after 1st .First() = {tripsAfterFirst}, after 2nd .First() = {tripsAfterSecond}");
            Console.WriteLine($"   ReferenceEquals(queryX1, queryX2) = {ReferenceEquals(queryX1, queryX2)} " +
                               "(both round-tripped, but materialization fixup still returned the tracked instance)");
            Console.WriteLine($"   ReferenceEquals(queryX1, order1a) = {ReferenceEquals(queryX1, order1a)} " +
                               "(same PK tracked earlier in this context, same instance)");
        }

        Console.WriteLine();
        Console.WriteLine("-- A SECOND, separate DbContext instance querying the same row --");
        using var context2 = new OrdersDbContext(DbInitializer.ConnectionString);
        var order2 = context2.Orders.Find(targetId);
        Console.WriteLine($"   ReferenceEquals(order1a, order2) = {ReferenceEquals(order1a, order2)} " +
                           "(different context -> different identity map -> different instance despite identical PK/data)");
        Console.WriteLine();
    }
}

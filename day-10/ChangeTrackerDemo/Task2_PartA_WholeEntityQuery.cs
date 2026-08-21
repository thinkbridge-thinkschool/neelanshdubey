namespace ChangeTrackerDemo;

public static class Task2_PartA_WholeEntityQuery
{
    // Returns the last logged "Executed DbCommand" SQL text, so Program.cs can print it
    // side-by-side against Part B's projected SQL.
    public static string Run()
    {
        Console.WriteLine("=== Part A: Whole-entity query - generated SQL ===");

        var sqlLog = new List<string>();
        void LogSql(string message)
        {
            if (message.Contains("Executed DbCommand"))
            {
                sqlLog.Add(message);
            }
        }

        using var context = new OrdersDbContext(DbInitializer.ConnectionString, LogSql, enableSensitiveDataLogging: true);

        Console.WriteLine("-- context.Orders.Where(o => o.Status == \"Shipped\").ToList() --");
        var orders = context.Orders.Where(o => o.Status == "Shipped").ToList();
        Console.WriteLine($"   Rows returned: {orders.Count}");

        Console.WriteLine();
        Console.WriteLine("Generated SQL:");
        Console.WriteLine(sqlLog[^1]);
        Console.WriteLine();

        return sqlLog[^1];
    }
}

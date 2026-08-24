namespace ChangeTrackerDemo;

public static class Task2_PartB_Projection
{
    public static string Run()
    {
        Console.WriteLine("=== Part B: Projected query (OrderSummaryDto) - generated SQL ===");

        var sqlLog = new List<string>();
        void LogSql(string message)
        {
            if (message.Contains("Executed DbCommand"))
            {
                sqlLog.Add(message);
            }
        }

        using var context = new OrdersDbContext(DbInitializer.ConnectionString, LogSql, enableSensitiveDataLogging: true);

        Console.WriteLine("-- context.Orders.Where(o => o.Status == \"Shipped\")");
        Console.WriteLine("--     .Select(o => new OrderSummaryDto(o.OrderId, o.OrderDate, o.Amount)).ToList() --");
        var summaries = context.Orders
            .Where(o => o.Status == "Shipped")
            .Select(o => new OrderSummaryDto(o.OrderId, o.OrderDate, o.Amount))
            .ToList();
        Console.WriteLine($"   Rows returned: {summaries.Count}");

        Console.WriteLine();
        Console.WriteLine("Generated SQL:");
        Console.WriteLine(sqlLog[^1]);
        Console.WriteLine();

        return sqlLog[^1];
    }

    public static void PrintComparison(string wholeEntitySql, string projectedSql)
    {
        Console.WriteLine("=== Side-by-side SQL comparison (Part A vs Part B) ===");
        Console.WriteLine("-- Whole-entity query: 5 columns (OrderId, CustomerId, OrderDate, Status, Amount) --");
        Console.WriteLine(wholeEntitySql);
        Console.WriteLine();
        Console.WriteLine("-- Projected query: 3 columns (OrderId, OrderDate, Amount only) --");
        Console.WriteLine(projectedSql);
        Console.WriteLine();
    }
}

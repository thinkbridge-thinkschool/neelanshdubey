using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public static class Task2_PartC_ClientEval
{
    // A plain C# method has no SQL translator - EF Core cannot inline it into a query.
    private static bool IsShippedStatus(string status) =>
        status.IndexOf("hip", StringComparison.OrdinalIgnoreCase) >= 0;

    public static void Run()
    {
        Console.WriteLine("=== Part C: Catching a client-side evaluation ===");

        var sqlLog = new List<string>();
        void LogSql(string message)
        {
            if (message.Contains("Executed DbCommand"))
            {
                sqlLog.Add(message);
            }
        }

        Console.WriteLine("-- BROKEN: context.Orders.Where(o => IsShippedStatus(o.Status)).ToList() --");
        Console.WriteLine("   (IsShippedStatus is a custom C# method call - EF has no translator for it)");
        using (var context = new OrdersDbContext(DbInitializer.ConnectionString, LogSql, enableSensitiveDataLogging: true))
        {
            try
            {
                var broken = context.Orders.Where(o => IsShippedStatus(o.Status)).ToList();
                Console.WriteLine($"   Unexpectedly succeeded with {broken.Count} row(s) - expected a translation failure.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("   Threw InvalidOperationException, as expected:");
                Console.WriteLine($"   {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("-- FIXED: context.Orders.Where(o => EF.Functions.Like(o.Status, \"%hip%\")).ToList() --");
        Console.WriteLine("   (EF.Functions.Like has a built-in translator -> SQL LIKE, pushed down to the database)");
        sqlLog.Clear();
        using (var context = new OrdersDbContext(DbInitializer.ConnectionString, LogSql, enableSensitiveDataLogging: true))
        {
            var fixedRows = context.Orders.Where(o => EF.Functions.Like(o.Status, "%hip%")).ToList();
            Console.WriteLine($"   Rows returned: {fixedRows.Count}");
            Console.WriteLine();
            Console.WriteLine("Generated SQL:");
            Console.WriteLine(sqlLog[^1]);
        }

        Console.WriteLine();
    }
}

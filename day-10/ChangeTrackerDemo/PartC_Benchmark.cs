using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

public record BenchmarkResult(string Label, double AvgMs, long AllocatedBytes, int Gen0, int Gen1, int Gen2, int MeasuredIterations);

public static class PartC_Benchmark
{
    public static void Run()
    {
        Console.WriteLine("=== Part C: Benchmark (10,000-row Orders table) ===");

        var tracked = RunBenchmark("Tracked .ToList() (default)", () =>
        {
            using var ctx = new OrdersDbContext(DbInitializer.ConnectionString);
            return ctx.Orders.ToList();
        });

        var untracked = RunBenchmark("AsNoTracking().ToList()", () =>
        {
            using var ctx = new OrdersDbContext(DbInitializer.ConnectionString);
            return ctx.Orders.AsNoTracking().ToList();
        });

        PrintComparisonTable(tracked, untracked);
    }

    // 1 warmup iteration (discarded) + `measuredIterations` measured iterations.
    private static BenchmarkResult RunBenchmark(string label, Func<List<Order>> action, int measuredIterations = 5)
    {
        Console.WriteLine($"-- Running: {label} --");

        // Warmup: pays for JIT, connection open, and query-plan caching so it doesn't
        // pollute the measured runs.
        action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var times = new List<double>(measuredIterations);
        var sw = new Stopwatch();

        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        for (var i = 0; i < measuredIterations; i++)
        {
            sw.Restart();
            var result = action();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            Console.WriteLine($"   iteration {i + 1}: {sw.Elapsed.TotalMilliseconds:F2} ms, {result.Count} rows");
        }

        var allocAfter = GC.GetAllocatedBytesForCurrentThread();
        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        return new BenchmarkResult(
            label,
            times.Average(),
            allocAfter - allocBefore,
            gen0After - gen0Before,
            gen1After - gen1Before,
            gen2After - gen2Before,
            measuredIterations);
    }

    private static void PrintComparisonTable(BenchmarkResult tracked, BenchmarkResult untracked)
    {
        Console.WriteLine();
        Console.WriteLine("-- Before/after comparison (averaged over measured iterations, 1 warmup discarded) --");
        Console.WriteLine($"{"Variant",-30}{"Avg ms",10}{"Alloc (MB)",14}{"Gen0",8}{"Gen1",8}{"Gen2",8}");
        Console.WriteLine(new string('-', 78));
        foreach (var r in new[] { tracked, untracked })
        {
            Console.WriteLine($"{r.Label,-30}{r.AvgMs,10:F2}{r.AllocatedBytes / 1024.0 / 1024.0,14:F2}{r.Gen0,8}{r.Gen1,8}{r.Gen2,8}");
        }

        var timeDeltaPct = (tracked.AvgMs - untracked.AvgMs) / tracked.AvgMs * 100;
        var allocDeltaPct = (tracked.AllocatedBytes - untracked.AllocatedBytes) / (double)tracked.AllocatedBytes * 100;
        Console.WriteLine();
        Console.WriteLine($"AsNoTracking is {timeDeltaPct:F1}% faster and allocates {allocDeltaPct:F1}% less than the tracked query " +
                           $"over {tracked.MeasuredIterations} measured iterations.");
        Console.WriteLine();
    }
}

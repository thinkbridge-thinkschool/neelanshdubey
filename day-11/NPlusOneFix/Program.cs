using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneFix;

public static class Program
{
    private const int SampleAuthorId = 250;
    private const int BenchmarkRuns = 50;
    private const int WarmupDiscard = 5;

    public static void Main(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "help";

        switch (command)
        {
            case "seed":
                RunSeed();
                break;
            case "baseline-count":
                RunBaselineCount();
                break;
            case "baseline-benchmark":
                RunBaselineBenchmark();
                break;
            case "fix-count":
                RunFixCount();
                break;
            case "fix-benchmark":
                RunFixBenchmark();
                break;
            case "sql-sample":
                PrintSqlSample();
                break;
            default:
                Console.WriteLine("Usage: dotnet run -- <seed|baseline-count|baseline-benchmark|fix-count|fix-benchmark|sql-sample>");
                break;
        }
    }

    private static void RunSeed()
    {
        using var context = new AppDbContext(Db.ConnectionString);
        Seeder.Seed(context);
    }

    // Part A: genuine N+1 - fetch all authors, then lazily touch Books per author.
    private static void RunBaselineCount()
    {
        var interceptor = new QueryCounterInterceptor();
        using var context = new AppDbContext(Db.ConnectionString, interceptor);

        var authors = context.Authors.ToList();
        long totalBooks = 0;
        foreach (var author in authors)
        {
            totalBooks += author.Books.Count();
        }

        Console.WriteLine($"Authors: {authors.Count}, Books counted: {totalBooks}");
        Console.WriteLine($"Query count (N+1 baseline): {interceptor.Count}");
    }

    private static void RunBaselineBenchmark()
    {
        var result = BenchmarkRunner.Run(BenchmarkRuns, WarmupDiscard, () =>
        {
            using var context = new AppDbContext(Db.ConnectionString);
            var sw = Stopwatch.StartNew();

            var authors = context.Authors.ToList();
            long totalBooks = 0;
            foreach (var author in authors)
            {
                totalBooks += author.Books.Count();
            }

            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        });

        PrintBenchmark("BASELINE (N+1)", result);
    }

    // Part B, variant 1: single query via projection - no per-row round trips.
    private static void RunFixCount()
    {
        var projectionInterceptor = new QueryCounterInterceptor();
        using (var context = new AppDbContext(Db.ConnectionString, projectionInterceptor))
        {
            var summaries = context.Authors
                .Select(a => new AuthorBookSummaryDto
                {
                    AuthorId = a.Id,
                    Name = a.Name,
                    BookCount = a.Books.Count(),
                    LatestBookTitle = a.Books
                        .OrderByDescending(b => b.PublishedYear)
                        .Select(b => b.Title)
                        .FirstOrDefault()
                })
                .ToList();

            Console.WriteLine($"Projection: {summaries.Count} summaries, query count: {projectionInterceptor.Count}");
        }

        // Part B, variant 2: Include + AsSplitQuery for comparison.
        var splitInterceptor = new QueryCounterInterceptor();
        using (var context = new AppDbContext(Db.ConnectionString, splitInterceptor))
        {
            var authors = context.Authors
                .AsNoTracking()
                .Include(a => a.Books)
                .AsSplitQuery()
                .ToList();

            Console.WriteLine($"Split query: {authors.Count} authors, query count: {splitInterceptor.Count}");
        }
    }

    private static void RunFixBenchmark()
    {
        var result = BenchmarkRunner.Run(BenchmarkRuns, WarmupDiscard, () =>
        {
            using var context = new AppDbContext(Db.ConnectionString);
            var sw = Stopwatch.StartNew();

            var summaries = context.Authors
                .Select(a => new AuthorBookSummaryDto
                {
                    AuthorId = a.Id,
                    Name = a.Name,
                    BookCount = a.Books.Count(),
                    LatestBookTitle = a.Books
                        .OrderByDescending(b => b.PublishedYear)
                        .Select(b => b.Title)
                        .FirstOrDefault()
                })
                .ToList();

            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        });

        PrintBenchmark("FIXED (projection + covering index)", result);
    }

    // The representative "get books for an author" query used for before/after execution plan capture.
    private static void PrintSqlSample()
    {
        using var context = new AppDbContext(Db.ConnectionString);
        var query = context.Books
            .Where(b => b.AuthorId == SampleAuthorId)
            .Select(b => new { b.Id, b.Title, b.PublishedYear });

        Console.WriteLine(query.ToQueryString());
    }

    private static void PrintBenchmark(string label, BenchmarkResult result)
    {
        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"p50: {result.P50:F3} ms");
        Console.WriteLine($"p95: {result.P95:F3} ms");
        Console.WriteLine($"p99: {result.P99:F3} ms");
    }
}

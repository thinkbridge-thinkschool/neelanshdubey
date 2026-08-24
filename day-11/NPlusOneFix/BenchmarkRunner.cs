namespace NPlusOneFix;

public record BenchmarkResult(double P50, double P95, double P99, List<double> Timings);

public static class BenchmarkRunner
{
    public static BenchmarkResult Run(int totalRuns, int warmupDiscard, Func<double> runOnceReturnsMs)
    {
        var timings = new List<double>();

        for (var i = 0; i < totalRuns; i++)
        {
            var elapsedMs = runOnceReturnsMs();
            if (i >= warmupDiscard)
            {
                timings.Add(elapsedMs);
            }
        }

        timings.Sort();
        return new BenchmarkResult(
            Percentile(timings, 50),
            Percentile(timings, 95),
            Percentile(timings, 99),
            timings);
    }

    private static double Percentile(List<double> sortedTimings, double p)
    {
        var index = (int)Math.Ceiling(p / 100.0 * sortedTimings.Count) - 1;
        index = Math.Clamp(index, 0, sortedTimings.Count - 1);
        return sortedTimings[index];
    }
}

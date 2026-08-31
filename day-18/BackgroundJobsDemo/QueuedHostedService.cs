using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackgroundJobsDemo;

/// <summary>
/// Drains <see cref="BackgroundTaskQueue"/> off the request thread. BackgroundService
/// (itself an IHostedService) runs ExecuteAsync as a fire-and-forget Task from StartAsync,
/// so the host finishes starting immediately instead of blocking on the loop.
/// </summary>
public sealed class QueuedHostedService : BackgroundService
{
    private readonly BackgroundTaskQueue _queue;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(BackgroundTaskQueue queue, ILogger<QueuedHostedService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Drain loop starting.");

        try
        {
            // stoppingToken is signalled by the base class's StopAsync (Ctrl+C / SIGTERM /
            // host.StopAsync). It cancels a pending wait for the next item...
            await foreach (var item in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    _logger.LogInformation("Processing {Name} (~{Ms}ms)", item.Name, item.Duration.TotalMilliseconds);

                    // ...and it also cancels work already in flight, so a slow job doesn't
                    // block shutdown forever.
                    await Task.Delay(item.Duration, stoppingToken);

                    _logger.LogInformation("Finished {Name}", item.Name);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("{Name} cancelled mid-flight by shutdown.", item.Name);
                    throw;
                }
                catch (Exception ex)
                {
                    // A single bad job must not take down the loop (or the process).
                    _logger.LogError(ex, "Job {Name} failed; continuing to drain remaining items.", item.Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: stoppingToken fired while we were waiting for the next item.
        }

        _logger.LogInformation("Drain loop exited.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Shutdown requested — {Count} item(s) still queued will NOT be started.",
            _queue.PendingCount);

        // base.StopAsync cancels stoppingToken and then awaits ExecuteAsync's Task,
        // bounded by HostOptions.ShutdownTimeout (or the token passed in here).
        await base.StopAsync(cancellationToken);
    }
}

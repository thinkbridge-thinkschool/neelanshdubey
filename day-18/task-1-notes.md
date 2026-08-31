# Day 18, Task 1: Background jobs

Project: [`day-18/BackgroundJobsDemo`](BackgroundJobsDemo) — a .NET 10 console app using
the Generic Host (`Microsoft.Extensions.Hosting` 10.0.0). It moves slow work off the
"request thread" onto a bounded in-memory queue drained by a `BackgroundService`, and
demonstrates a clean shutdown via the cancellation token.

## The queue

[`BackgroundTaskQueue.cs`](BackgroundJobsDemo/BackgroundTaskQueue.cs) wraps a bounded
`System.Threading.Channels.Channel<WorkItem>` (`FullMode = Wait`) so a slow consumer
back-pressures producers instead of the queue growing unbounded. `EnqueueAsync` writes;
`ReadAllAsync(CancellationToken)` gives the consumer an `IAsyncEnumerable<WorkItem>` that
respects cancellation while waiting for the next item.

## The BackgroundService

```csharp
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
```

Registration ([`Program.cs`](BackgroundJobsDemo/Program.cs)):

```csharp
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddSingleton<BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();
```

### How it shuts down cleanly

1. The host (Ctrl+C, SIGTERM, or an explicit `host.StopAsync()`) cancels the token it
   handed to `ExecuteAsync` as `stoppingToken`.
2. That single token does double duty: it aborts `await foreach` while it's waiting on
   an empty queue, *and* it aborts `Task.Delay(item.Duration, stoppingToken)` if a job is
   already running — so shutdown isn't blocked by whatever job happens to be mid-flight.
3. The `OperationCanceledException` from the in-flight job is caught, logged as a
   mid-flight cancellation, and rethrown so the outer loop also exits instead of trying
   to dequeue the next item.
4. `QueuedHostedService.StopAsync` is overridden only to log how many items are still
   queued and unstarted, then delegates to `base.StopAsync`, which is what actually
   awaits the `ExecuteAsync` task — bounded by `HostOptions.ShutdownTimeout` (set to 3s
   here) so a stuck job can't hang the process forever.
5. Items already sitting in the channel when shutdown starts are simply dropped — an
   in-memory queue has no persistence, which is the sharpest limitation of this pattern
   (see Hangfire note below).

Observed run (`dotnet run`, five jobs enqueued, `host.StopAsync()` called ~1.2s later
while the 5-second `generate-report` job is running):

```
info: BackgroundJobsDemo.QueuedHostedService[0]
      Drain loop starting.
info: Program[0]
      Enqueued send-welcome-email
info: Program[0]
      Enqueued resize-thumbnail
info: Program[0]
      Enqueued generate-report
info: Program[0]
      Enqueued sync-inventory
info: Program[0]
      Enqueued notify-webhook
info: BackgroundJobsDemo.QueuedHostedService[0]
      Processing send-welcome-email (~300ms)
info: BackgroundJobsDemo.QueuedHostedService[0]
      Finished send-welcome-email
info: BackgroundJobsDemo.QueuedHostedService[0]
      Processing resize-thumbnail (~400ms)
info: BackgroundJobsDemo.QueuedHostedService[0]
      Finished resize-thumbnail
info: BackgroundJobsDemo.QueuedHostedService[0]
      Processing generate-report (~5000ms)
warn: Program[0]
      --- Simulating Ctrl+C / SIGTERM: calling host.StopAsync() ---
info: Microsoft.Hosting.Lifetime[0]
      Application is shutting down...
warn: BackgroundJobsDemo.QueuedHostedService[0]
      Shutdown requested — 2 item(s) still queued will NOT be started.
warn: BackgroundJobsDemo.QueuedHostedService[0]
      generate-report cancelled mid-flight by shutdown.
info: BackgroundJobsDemo.QueuedHostedService[0]
      Drain loop exited.
info: Program[0]
      Host stopped. Jobs 4 and 5 were never started — that's the tradeoff of an in-memory queue.
```

Jobs 1–2 (short) complete normally, job 3 (`generate-report`, 5s) is cancelled mid-`Task.Delay`
the moment shutdown starts, and jobs 4–5 never leave the channel — `host.StopAsync()`
returns in ~1.2s total instead of waiting out the full 5-second job.

## IHostedService vs BackgroundService vs Hangfire

- **`IHostedService`** is the raw interface: `StartAsync`/`StopAsync`. If you implement it
  directly for a long-running loop, you own the "don't block `StartAsync`" problem
  yourself — typically by kicking off `Task.Run(...)` inside `StartAsync` and awaiting it
  (with a timeout) inside `StopAsync`. That's exactly the boilerplate `BackgroundService`
  already provides.
- **`BackgroundService`** is an abstract `IHostedService` that does that boilerplate for
  you: `StartAsync` fires `ExecuteAsync(stoppingToken)` as a background `Task` and returns
  immediately (host startup isn't blocked); `StopAsync` signals `stoppingToken` and awaits
  that task up to `HostOptions.ShutdownTimeout`. It's the right default for an in-process
  loop like the queue drain above — no persistence, no retry policy, no scheduling UI,
  lives and dies with the process.
- **Hangfire** adds a persistent job store (SQL Server, Redis, etc.), automatic retries
  with backoff, a dashboard, and cron-style recurring/delayed scheduling — jobs survive an
  app restart or crash because they're durable rows in storage, not just entries in an
  in-process `Channel<T>`.

**One line: when Hangfire over a hosted service?** Reach for Hangfire the moment a job
needs to survive a process restart, retry automatically on failure, run on a schedule
(cron/delayed), or be visible/manageable outside the app (dashboard) — a `BackgroundService`
is enough only when losing queued-but-unstarted work on shutdown/crash (as shown above) is
acceptable.

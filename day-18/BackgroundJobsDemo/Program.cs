using BackgroundJobsDemo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    // Ceiling on how long host.StopAsync() waits for QueuedHostedService.StopAsync
    // (i.e. for ExecuteAsync to observe cancellation and return) before giving up.
    options.ShutdownTimeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddSingleton<BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();

using var host = builder.Build();
await host.StartAsync();

var queue = host.Services.GetRequiredService<BackgroundTaskQueue>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// Simulate API request threads handing slow work off to the queue instead of
// awaiting it inline.
WorkItem[] jobs =
[
    new(1, "send-welcome-email", TimeSpan.FromMilliseconds(300)),
    new(2, "resize-thumbnail", TimeSpan.FromMilliseconds(400)),
    new(3, "generate-report", TimeSpan.FromSeconds(5)), // will be mid-flight at shutdown
    new(4, "sync-inventory", TimeSpan.FromMilliseconds(300)),
    new(5, "notify-webhook", TimeSpan.FromMilliseconds(300)),
];

foreach (var job in jobs)
{
    await queue.EnqueueAsync(job);
    logger.LogInformation("Enqueued {Name}", job.Name);
}
queue.Complete();

await Task.Delay(1200); // jobs 1-2 finish; job 3 is now ~500ms into its 5s delay

logger.LogWarning("--- Simulating Ctrl+C / SIGTERM: calling host.StopAsync() ---");
await host.StopAsync();

logger.LogInformation("Host stopped. Jobs 4 and 5 were never started — that's the tradeoff of an in-memory queue.");

using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace QuotesApi.Extensions;

// Day 5 Task 6: registers the one outbound HTTP dependency this API has, wrapped in a
// Polly resilience pipeline (retry -> circuit breaker -> timeout). See the Task 6 write-up
// for why "external-quotes" exists at all — QuotesApi had no outbound HTTP call before this.
public static class HttpClientExtensions
{
    private const int MaxRetryAttempts = 3;

    public static IServiceCollection AddExternalQuotesClient(this IServiceCollection services)
    {
        services.AddHttpClient("external-quotes", client =>
        {
            client.BaseAddress = new Uri("https://zenquotes.io/");
        })
        .AddResilienceHandler("default", (pipeline, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ExternalQuotes.Resilience");

            pipeline
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        var host = args.Context.GetRequestMessage()?.RequestUri?.Host ?? "unknown-host";

                        logger.LogWarning(
                            args.Outcome.Exception,
                            "Retry {AttemptNumber}/{MaxAttempts} calling {Host} after {DelayMs:F0}ms backoff — last result: {StatusCode}",
                            args.AttemptNumber + 1,
                            MaxRetryAttempts,
                            host,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Result?.StatusCode.ToString() ?? "no response (exception)");

                        return ValueTask.CompletedTask;
                    }
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    // 10: low enough that a handful of real requests during a genuine outage
                    // still trips the breaker, high enough that 1-2 blips on a quiet client
                    // don't. Polly's own default (100) assumes much higher-traffic services
                    // than this one outbound dependency sees.
                    MinimumThroughput = 10,
                    // 30s: matches the sampling window — gives the dependency a full window
                    // to recover before the breaker lets a probe request through again.
                    BreakDuration = TimeSpan.FromSeconds(30),
                    OnOpened = args =>
                    {
                        var host = args.Context.GetRequestMessage()?.RequestUri?.Host ?? "unknown-host";

                        logger.LogError(
                            "Circuit breaker OPEN for {Host} for {BreakDurationSeconds}s — failure ratio exceeded 50% over the last 30s window. Last outcome: {StatusCode} {Exception}",
                            host,
                            args.BreakDuration.TotalSeconds,
                            args.Outcome.Result?.StatusCode.ToString() ?? "no response (exception)",
                            args.Outcome.Exception?.Message ?? "n/a");

                        return ValueTask.CompletedTask;
                    },
                    OnClosed = args =>
                    {
                        logger.LogInformation("Circuit breaker CLOSED — target host has recovered");
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(10));
        });

        return services;
    }
}

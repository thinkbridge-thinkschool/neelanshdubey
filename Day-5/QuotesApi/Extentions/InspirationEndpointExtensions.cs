using Polly.CircuitBreaker;
using Polly.Timeout;

namespace QuotesApi.Extensions;

// Day 5 Task 6: this endpoint was added to demonstrate the resilience pattern, since
// QuotesApi had no outbound HTTP call anywhere in its existing code before this task —
// see the Task 6 write-up. It is not part of the app's prior functionality.
public static class InspirationEndpointExtensions
{
    public static WebApplication MapInspirationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/quotes/inspiration", async (
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            var client = httpClientFactory.CreateClient("external-quotes");

            try
            {
                var response = await client.GetAsync("api/random", cancellationToken);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Results.Content(body, "application/json");
            }
            catch (Exception ex) when (
                ex is HttpRequestException
                or TimeoutRejectedException
                or BrokenCircuitException)
            {
                return Results.Problem(
                    title: "External quotes service is unavailable",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous(); // Read-only proxy to a public external API, no app data or caller identity involved — same as GET /api/quotes.

        return app;
    }
}

using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Authorization;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) =>
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration);

// Only reach for Key Vault when a URI is actually configured (Development only, see appsettings).
// Unconditionally calling AddAzureKeyVault would make DefaultAzureCredential try every credential
// source it knows (env vars, managed identity, Azure CLI, interactive browser...) on every app
// startup, including in CI where none of them exist — that's slow at best and can hang the test
// host at worst.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

var appInsightsConnectionString = builder.Configuration["AppInsights:ConnectionString"];

var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("QuotesApi"));

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    // The real extension method is UseAzureMonitor(), not AddAzureMonitor(). This single call
    // wires traces + metrics + logs to App Insights, AND already includes AspNetCore + HttpClient
    // instrumentation out of the box. Do not add those again in WithTracing below, or every
    // request produces two sets of spans in App Insights.
    otelBuilder.UseAzureMonitor(o => o.ConnectionString = appInsightsConnectionString);
}

otelBuilder.WithTracing(t =>
{
    t.AddEntityFrameworkCoreInstrumentation()
     .AddSource("QuotesApi");

    if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
    {
        // Only add these manually for local dev (Jaeger/Aspire), where UseAzureMonitor isn't
        // already providing them.
        t.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation();
    }

    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
    {
        // The OTLP exporter defaults to gRPC, which requires HTTP/2 cleartext support
        // when the endpoint is plain http:// (as it is for local collectors like Jaeger/Aspire).
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    }
});

builder.Services.AddSingleton<IAuthorizationHandler, SameOwnerAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("can-edit-quotes", policy =>
        policy.RequireClaim("scope", "quotes.write"));

    options.AddPolicy("can-delete-own-quote", policy =>
        policy.Requirements.Add(new SameOwnerRequirement()));
});

var app = builder.Build();

app.Use((context, next) =>
{
    var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    using (Serilog.Context.LogContext.PushProperty("TraceId", traceId))
    {
        return next();
    }
});

app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.ApplyMigrations();
}

app.MapGet("/", () => "Quotes API is running!");

app.MapAuthEndpoints();
app.MapQuoteEndpoints();

app.Run();
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

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
app.MapAuthorEndpoints();

app.Run();
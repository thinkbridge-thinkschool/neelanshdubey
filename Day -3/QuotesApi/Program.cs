using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IAuthorizationHandler, SameOwnerAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, SameOwnerCollectionAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("can-edit-quotes", policy =>
        policy.RequireClaim("scope", "quotes.write"));

    options.AddPolicy("can-delete-own-quote", policy =>
        policy.Requirements.Add(new SameOwnerRequirement()));

    options.AddPolicy("can-manage-own-collection", policy =>
        policy.Requirements.Add(new SameOwnerRequirement()));
});

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
app.MapCollectionEndpoints();

app.Run();
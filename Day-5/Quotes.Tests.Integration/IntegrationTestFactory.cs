using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Services;

namespace Quotes.Tests.Integration;

// One instance of this factory is created per test method (see
// IntegrationTestBase), each backed by its own SQLite ":memory:" connection.
// The connection is opened here and kept open for the factory's lifetime,
// since an in-memory SQLite database is destroyed the moment its connection
// closes; EF Core will not close a connection it did not open itself, so
// handing it an already-open connection keeps the data alive across requests
// within the same test while guaranteeing a brand-new, empty, and fully
// isolated database per test (no shared file, no cross-test leakage).
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeClock Clock { get; } = new();

    public IntegrationTestFactory()
    {
        _connection.Open();

        // Jwt:SigningKey is intentionally absent from appsettings.json (see
        // JwtSettings.cs); Program.cs reads it eagerly in AddInfrastructure,
        // before this factory's config overrides become visible, so it must
        // be supplied as an env var instead.
        Environment.SetEnvironmentVariable(
            "Jwt__SigningKey", "test-only-signing-key-not-for-production-1234567890");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

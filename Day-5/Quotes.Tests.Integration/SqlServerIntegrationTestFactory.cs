using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Services;

namespace Quotes.Tests.Integration;

// Mirrors IntegrationTestFactory but points AppDbContext at a real SQL
// Server database (a uniquely-named database on the shared container from
// SqlServerContainerFixture) instead of an in-memory SQLite connection.
//
// Schema is applied via EnsureCreated() from the current model snapshot,
// not Database.Migrate() - the existing EF migrations under
// QuotesApi/Migrations were scaffolded against the SQLite provider and bake
// in SQLite-specific column types ("INTEGER"/"TEXT") and a
// "Sqlite:Autoincrement" annotation that the SQL Server provider ignores.
// Running them as-is would create tables whose primary-key columns lack
// IDENTITY, so the first insert relying on ValueGeneratedOnAdd would fail.
// This means SQL Server migration-script correctness itself is NOT
// exercised by this suite - only runtime query/business-logic behavior
// against a real SQL Server engine is. See the report for the full
// tradeoff discussion.
public class SqlServerIntegrationTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public FakeClock Clock { get; } = new();

    public SqlServerIntegrationTestFactory(string connectionString)
    {
        _connectionString = connectionString;

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

            // AddDbContext registers the options-configuring delegate via
            // IDbContextOptionsConfiguration<TContext>, which accumulates
            // across multiple AddDbContext calls instead of being replaced.
            // Without removing it here, Program.cs's UseSqlite(...) call
            // still runs alongside UseSqlServer(...) below, applying both
            // provider extensions to the same DbContextOptions and causing
            // EF to throw ("services for database providers ... have been
            // registered") the moment AppDbContext is resolved.
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_connectionString));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }
}

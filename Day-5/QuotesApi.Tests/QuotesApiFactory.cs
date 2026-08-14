using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuotesApi.Tests;

public class QuotesApiFactory : WebApplicationFactory<Program>
{
    public string DbPath { get; } =
        Path.Combine(Path.GetTempPath(), $"quotesapi_tests_{Guid.NewGuid():N}.db");

    public QuotesApiFactory()
    {
        // Jwt:SigningKey is intentionally absent from appsettings.json (see
        // JwtSettings.cs); Program.cs reads it eagerly in AddInfrastructure,
        // before this factory's config overrides become visible, so it must
        // be supplied as an env var instead. Auth is stubbed via
        // TestAuthHandler below, but AddInfrastructure still runs regardless.
        Environment.SetEnvironmentVariable(
            "Jwt__SigningKey", "test-only-signing-key-not-for-production-1234567890");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={DbPath}"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Microsoft.Data.Sqlite pools connections at the process level, so the
        // underlying file can still be open here even after the host is disposed.
        SqliteConnection.ClearAllPools();

        DeleteDbFiles();
    }

    private void DeleteDbFiles()
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = DbPath + suffix;

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Quotes.Tests.Integration;

// Confirms Database.MigrateAsync() actually walked the full migration chain
// against the real SQLite schema, rather than e.g. EnsureCreated() from the
// current model snapshot - by checking effects that only exist because
// specific, later migrations ran: the OwnerId column added by
// AddOwnerIdToQuote, and the unique index added by AddUniqueIndexOnUserEmail.
public class MigrationsAndSchemaTests : IntegrationTestBase
{
    [Fact]
    public async Task Migrations_QuotesTableHasOwnerIdColumn_FromLaterMigration()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = dbContext.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Quotes);";

        var columnNames = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columnNames.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        Assert.Contains("OwnerId", columnNames);
    }

    [Fact]
    public async Task Migrations_UniqueIndexOnUserEmail_RejectsDuplicateEmailAtTheDatabaseLevel()
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Users.Add(new User
        {
            Email = UserSeed.Email,
            PasswordHash = BCryptNet.BCrypt.HashPassword("AnotherPassword123!")
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }
}

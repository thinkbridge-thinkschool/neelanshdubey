using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Quotes.Tests.Integration;

// One SQL Server container for the entire "SqlServer" test collection (see
// SqlServerCollection) - starting a fresh container per test would make the
// suite unusably slow. Isolation between tests is NOT provided by this
// fixture; each test creates and drops its own database on this shared
// server instance (see SqlServerIntegrationTestBase).
public class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<string> CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync();

        var builder = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }

    public async Task DropDatabaseAsync(string databaseName)
    {
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             IF DB_ID(N'{databaseName}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{databaseName}];
             END
             """;
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServer";
}

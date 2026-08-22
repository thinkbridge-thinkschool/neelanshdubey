using System.Data;
using System.Data.Common;

namespace Quotes.Tests.Integration;

// EF's DbCommandInterceptor only fires for commands EF itself creates and
// executes through its own RelationalCommand pipeline - confirmed
// empirically that it stays at 0 when Dapper issues a query directly
// against the same underlying connection, since Dapper calls
// CreateCommand()/ExecuteReader on the IDbConnection itself and never goes
// through EF's command-building code. This wrapper counts SQL round trips
// at the raw ADO.NET level instead, for use as the connection handed to
// Dapper. It forwards every operation to the real connection unchanged -
// it never opens, closes, or disposes the inner connection itself, since
// that connection is owned and kept alive elsewhere (the test's in-memory
// SQLite connection).
public sealed class CountingDbConnection : DbConnection
{
    private readonly DbConnection _inner;

    public int Count { get; private set; }

    public CountingDbConnection(DbConnection inner)
    {
        _inner = inner;
    }

    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }

    public override string Database => _inner.Database;
    public override string DataSource => _inner.DataSource;
    public override string ServerVersion => _inner.ServerVersion;
    public override ConnectionState State => _inner.State;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public override void Close() => _inner.Close();
    public override void Open() => _inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        _inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() => new CountingDbCommand(_inner.CreateCommand(), this);

    protected override void Dispose(bool disposing)
    {
        // Deliberately not disposing _inner: this wrapper never owned it.
        base.Dispose(disposing);
    }

    private void RecordExecution() => Count++;

    private sealed class CountingDbCommand : DbCommand
    {
        private readonly DbCommand _inner;
        private readonly CountingDbConnection _owner;

        public CountingDbCommand(DbCommand inner, CountingDbConnection owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public override string CommandText
        {
            get => _inner.CommandText;
            set => _inner.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => _inner.CommandTimeout;
            set => _inner.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => _inner.CommandType;
            set => _inner.CommandType = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => _inner.UpdatedRowSource;
            set => _inner.UpdatedRowSource = value;
        }

        public override bool DesignTimeVisible
        {
            get => _inner.DesignTimeVisible;
            set => _inner.DesignTimeVisible = value;
        }

        protected override DbConnection? DbConnection
        {
            get => _owner;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => _inner.Transaction;
            set => _inner.Transaction = value;
        }

        public override void Cancel() => _inner.Cancel();

        public override int ExecuteNonQuery()
        {
            _owner.RecordExecution();
            return _inner.ExecuteNonQuery();
        }

        public override object? ExecuteScalar()
        {
            _owner.RecordExecution();
            return _inner.ExecuteScalar();
        }

        public override void Prepare() => _inner.Prepare();

        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            _owner.RecordExecution();
            return _inner.ExecuteReader(behavior);
        }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            _owner.RecordExecution();
            return await _inner.ExecuteReaderAsync(behavior, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }
    }
}

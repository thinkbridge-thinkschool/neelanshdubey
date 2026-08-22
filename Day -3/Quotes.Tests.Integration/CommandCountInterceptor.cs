using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Quotes.Tests.Integration;

// Counts how many SQL statements a DbContext actually sends to the
// database - used to prove the read-model query is a single round trip
// rather than N+1 or a split query.
public sealed class CommandCountInterceptor : DbCommandInterceptor
{
    public int Count { get; private set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Count++;
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

using System.Threading.Channels;

namespace BackgroundJobsDemo;

/// <summary>
/// In-memory hand-off point between request threads and the drain loop.
/// Bounded + Wait so a slow consumer applies back-pressure to producers
/// instead of letting the queue grow without limit.
/// </summary>
public sealed class BackgroundTaskQueue
{
    private readonly Channel<WorkItem> _channel;

    public BackgroundTaskQueue(int capacity = 100)
    {
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public int PendingCount => _channel.Reader.Count;

    public ValueTask EnqueueAsync(WorkItem item, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<WorkItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>No more items will be produced; lets the reader's foreach end once drained.</summary>
    public void Complete() => _channel.Writer.Complete();
}

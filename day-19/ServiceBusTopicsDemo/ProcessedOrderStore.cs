using System.Collections.Concurrent;

namespace ServiceBusTopicsDemo;

/// <summary>
/// Stand-in for a shared dedupe store (Redis/SQL in production). A single in-process
/// ConcurrentDictionary is enough to prove the idempotency logic, but it only works because
/// this demo runs all competing consumers in one process — real competing consumers running
/// as separate processes/instances need an external store so they see each other's dedupe state.
/// </summary>
public sealed class ProcessedOrderStore
{
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new();

    public bool IsAlreadyProcessed(string messageId) => _processedMessageIds.ContainsKey(messageId);

    public void MarkProcessed(string messageId) => _processedMessageIds.TryAdd(messageId, 0);
}

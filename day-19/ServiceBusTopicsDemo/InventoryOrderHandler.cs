using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusTopicsDemo;

/// <summary>
/// One instance runs per competing-consumer worker; all instances share the same
/// ProcessedOrderStore so a duplicate delivered to a different worker is still caught.
/// </summary>
public sealed class InventoryOrderHandler(string workerId, ProcessedOrderStore store, ILogger logger)
{
    public async Task HandleAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        // Check-before-process, mark-after-success: marking the id "processed" up front (before
        // we know parsing/validation will succeed) would falsely dedupe a poison message on its
        // second delivery attempt — it'd get silently completed instead of retried/dead-lettered.
        if (store.IsAlreadyProcessed(message.MessageId))
        {
            logger.LogWarning("[{WorkerId}] Duplicate OrderId={OrderId} (delivery #{Count}) — already processed, skipping.",
                workerId, message.MessageId, message.DeliveryCount);
            await args.CompleteMessageAsync(message, args.CancellationToken);
            return;
        }

        OrderMessage order;
        try
        {
            order = JsonSerializer.Deserialize<OrderMessage>(message.Body)
                ?? throw new JsonException("Body deserialized to null.");
        }
        catch (JsonException ex)
        {
            // Unparseable body: retrying the exact same bytes will never succeed, but we still
            // let the subscription's MaxDeliveryCount policy drive the outcome (abandon here,
            // Service Bus auto-dead-letters once delivery count is exceeded) to demonstrate that
            // automatic path, as distinct from the immediate manual dead-letter below.
            logger.LogError(ex, "[{WorkerId}] Poison message OrderId={OrderId} (delivery #{Count}): body is not valid JSON.",
                workerId, message.MessageId, message.DeliveryCount);
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
            return;
        }

        if (order.Quantity <= 0)
        {
            // A business-rule violation is permanent — no amount of retrying fixes a negative
            // quantity, so dead-letter immediately instead of burning through MaxDeliveryCount.
            logger.LogError("[{WorkerId}] Rejecting OrderId={OrderId}: invalid Quantity={Quantity}. Dead-lettering immediately.",
                workerId, order.OrderId, order.Quantity);
            await args.DeadLetterMessageAsync(message,
                deadLetterReason: "InvalidQuantity",
                deadLetterErrorDescription: $"Quantity must be positive, got {order.Quantity}.",
                cancellationToken: args.CancellationToken);
            return;
        }

        store.MarkProcessed(message.MessageId);
        logger.LogInformation("[{WorkerId}] Processed OrderId={OrderId} Sku={Sku} Qty={Quantity}.",
            workerId, order.OrderId, order.Sku, order.Quantity);

        if (order.Sku == RedeliveryDrill.MarkerSku && message.DeliveryCount == 1)
        {
            // Simulate the classic at-least-once failure mode: processing succeeded, but the
            // ack (CompleteMessageAsync) is lost before it reaches the broker — e.g. the process
            // crashes right after committing side effects. Service Bus redelivers the exact same
            // message; abandoning here (instead of completing) reproduces that redelivery on
            // demand so the dedupe check above is what has to catch it, not entity-level
            // duplicate detection on a re-publish (which never even reaches a handler).
            logger.LogWarning("[{WorkerId}] Simulating a lost ack for OrderId={OrderId} — abandoning after successful processing so it gets redelivered.",
                workerId, order.OrderId);
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
            return;
        }

        await args.CompleteMessageAsync(message, args.CancellationToken);
    }
}

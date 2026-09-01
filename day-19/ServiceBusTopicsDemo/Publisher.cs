using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusTopicsDemo;

public static class Publisher
{
    public static async Task SendDemoBatchAsync(ServiceBusClient client, string topicName, ILogger logger)
    {
        await using var sender = client.CreateSender(topicName);

        var orders = Enumerable.Range(1, 3)
            .Select(i => new OrderMessage(Guid.NewGuid().ToString(), $"SKU-{1000 + i}", i * 2, DateTimeOffset.UtcNow))
            .ToList();

        foreach (var order in orders)
        {
            await SendOrderAsync(sender, order, logger);
        }

        // Duplicate publish: same OrderId (= MessageId) as orders[0], simulating a retried send
        // (e.g. the publisher timed out waiting for the broker's ack and resent). Because the
        // topic has RequiresDuplicateDetection enabled, Service Bus itself swallows this within
        // the 10-minute detection window — it never becomes a second message, so no consumer
        // ever sees it. That's broker-level dedup; see RedeliveryDrill for the handler-level
        // dedupe proof, which covers the case broker-level dedup can't (redelivery of a message
        // already accepted by the broker, not a second publish of it).
        logger.LogInformation("Re-sending OrderId={OrderId} to simulate a duplicate publish (broker-level dedup).", orders[0].OrderId);
        await SendOrderAsync(sender, orders[0], logger);

        // Handler-level idempotency drill: this order's Sku tells the handler to abandon it
        // (instead of completing) right after successfully processing it the first time,
        // simulating a lost ack. Service Bus redelivers the same message; the handler's own
        // dedupe check — not the broker's — has to recognize it and skip reprocessing.
        var redeliveryDrillOrder = new OrderMessage(Guid.NewGuid().ToString(), RedeliveryDrill.MarkerSku, 1, DateTimeOffset.UtcNow);
        logger.LogInformation("Sending OrderId={OrderId} to drill handler-level dedupe via a simulated lost ack.", redeliveryDrillOrder.OrderId);
        await SendOrderAsync(sender, redeliveryDrillOrder, logger);

        // Business-rule poison message: negative quantity. The handler dead-letters it
        // immediately without retrying, since no retry count will make it valid.
        var badQuantityOrder = new OrderMessage(Guid.NewGuid().ToString(), "SKU-9999", -5, DateTimeOffset.UtcNow);
        logger.LogInformation("Sending OrderId={OrderId} with invalid Quantity=-5.", badQuantityOrder.OrderId);
        await SendOrderAsync(sender, badQuantityOrder, logger);

        // Malformed-body poison message: not valid JSON at all. The handler abandons it on every
        // delivery attempt; once the subscription's MaxDeliveryCount (3) is exceeded, Service Bus
        // dead-letters it automatically with reason "MaxDeliveryCountExceeded".
        var poisonMessageId = Guid.NewGuid().ToString();
        logger.LogInformation("Sending malformed-JSON poison message MessageId={MessageId}.", poisonMessageId);
        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromString("{ this is not json"))
        {
            MessageId = poisonMessageId,
            ContentType = "application/json"
        });

        logger.LogInformation("Publish batch complete.");
    }

    private static async Task SendOrderAsync(ServiceBusSender sender, OrderMessage order, ILogger logger)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(order))
        {
            // Doubles as the dedupe key for the consumer's handler-level idempotency check and,
            // since the topic has RequiresDuplicateDetection enabled, for Service Bus's own
            // entity-level duplicate detection within its 10-minute history window.
            MessageId = order.OrderId,
            ContentType = "application/json"
        };
        await sender.SendMessageAsync(message);
        logger.LogInformation("Sent OrderId={OrderId} Sku={Sku} Qty={Quantity}.", order.OrderId, order.Sku, order.Quantity);
    }
}

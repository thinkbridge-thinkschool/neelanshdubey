using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusTopicsDemo;

/// <summary>
/// A second, independent subscription on the same topic — proves topic fan-out: every message
/// published once reaches both subscriptions, each with its own delivery/redelivery state.
/// </summary>
public sealed class AuditOrderHandler(ILogger logger)
{
    public async Task HandleAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        logger.LogInformation(
            "[audit] Saw OrderId={OrderId} (delivery #{Count}) on its own subscription — independent of inventory-sub's processing.",
            message.MessageId, message.DeliveryCount);
        await args.CompleteMessageAsync(message, args.CancellationToken);
    }
}

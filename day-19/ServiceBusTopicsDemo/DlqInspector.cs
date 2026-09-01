using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusTopicsDemo;

public static class DlqInspector
{
    public static async Task PrintDeadLettersAsync(ServiceBusClient client, string topicName, string subscriptionName, ILogger logger)
    {
        await using var receiver = client.CreateReceiver(topicName, subscriptionName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter
        });

        var messages = await receiver.ReceiveMessagesAsync(maxMessages: 20, maxWaitTime: TimeSpan.FromSeconds(5));
        if (messages.Count == 0)
        {
            logger.LogInformation("Dead-letter queue for {Topic}/{Subscription} is empty.", topicName, subscriptionName);
            return;
        }

        logger.LogWarning("Found {Count} dead-lettered message(s) in {Topic}/{Subscription}/$DeadLetterQueue:",
            messages.Count, topicName, subscriptionName);

        foreach (var message in messages)
        {
            logger.LogWarning(
                "  MessageId={MessageId} DeliveryCount={DeliveryCount} Reason={Reason} Description={Description} Body={Body}",
                message.MessageId,
                message.DeliveryCount,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription,
                message.Body.ToString());

            // Complete (remove) it from the DLQ once we've captured proof, so a re-run of
            // drain-dlq doesn't just print the same messages again.
            await receiver.CompleteMessageAsync(message);
        }
    }
}

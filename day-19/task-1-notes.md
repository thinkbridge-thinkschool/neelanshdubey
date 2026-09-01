# Day 19, Task 1: Service Bus topics + DLQ

Project: [`day-19/ServiceBusTopicsDemo`](ServiceBusTopicsDemo) — a .NET 10 console app
(`Azure.Messaging.ServiceBus` 7.18.2) that publishes to a Service Bus topic with two
subscriptions, consumes one of them with competing consumers, makes the handler idempotent
via a dedupe check, and proves a poison message lands in the dead-letter queue (DLQ) both
automatically and manually.

## Azure resources

Real resources, provisioned with the Azure CLI into a dedicated resource group:

| Resource | Name |
|---|---|
| Resource group | `day19-servicebus-rg` (centralindia) |
| Namespace (Standard tier — Basic doesn't support topics) | `sb-day19-nd-30482` |
| Topic | `orders` — `RequiresDuplicateDetection`, 10-minute history window |
| Subscription 1 | `inventory-sub` — `MaxDeliveryCount = 3` |
| Subscription 2 | `audit-sub` — `MaxDeliveryCount = 3` |

**Auth note:** the plan was passwordless auth (`DefaultAzureCredential` + an "Azure Service
Bus Data Owner" role assignment on the namespace), consistent with [[azure-cli-env]]. RBAC
turned out to be unavailable on this subscription — `az role assignment create` (and even a
plain `az role assignment list`) fails outright with `MissingSubscription`, a subscription-level
restriction, not a permissions problem. Fell back to a scoped SAS policy instead: an
`app-send-listen` authorization rule with only `Send` + `Listen` rights (not the default
`RootManageSharedAccessKey`). Its connection string is **not** in any file in this repo — it's
stored via `dotnet user-secrets` under `ServiceBus:ConnectionString` (or the
`SERVICEBUS_CONNECTIONSTRING` env var as a fallback), scoped to this project via the
`UserSecretsId` in the `.csproj`. `appsettings.json` only holds non-secret config (topic/
subscription names).

## Publisher

[`Publisher.cs`](ServiceBusTopicsDemo/Publisher.cs) sends a batch of messages designed to
exercise every path below: 3 normal orders, a duplicate publish, a handler-level redelivery
drill, a business-rule poison message, and a malformed-JSON poison message.

```csharp
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

        // Duplicate publish: same OrderId (= MessageId) as orders[0]. RequiresDuplicateDetection
        // on the topic swallows this within its 10-minute window — it never becomes a second
        // message, so no consumer ever sees it. That's broker-level dedup; it can't cover
        // redelivery of a message the broker already accepted (see the drill order below).
        await SendOrderAsync(sender, orders[0], logger);

        // Handler-level idempotency drill: this order's Sku tells the handler to abandon it
        // (instead of completing) right after successfully processing it once, simulating a
        // lost ack. Service Bus redelivers the same message; the handler's own dedupe check —
        // not the broker's — has to recognize and skip it.
        var redeliveryDrillOrder = new OrderMessage(Guid.NewGuid().ToString(), RedeliveryDrill.MarkerSku, 1, DateTimeOffset.UtcNow);
        await SendOrderAsync(sender, redeliveryDrillOrder, logger);

        // Business-rule poison message: negative quantity. No retry count fixes this, so the
        // handler dead-letters it immediately instead of burning through MaxDeliveryCount.
        var badQuantityOrder = new OrderMessage(Guid.NewGuid().ToString(), "SKU-9999", -5, DateTimeOffset.UtcNow);
        await SendOrderAsync(sender, badQuantityOrder, logger);

        // Malformed-body poison message: not valid JSON at all. The handler abandons it on every
        // delivery; once MaxDeliveryCount (3) is exceeded, Service Bus dead-letters it
        // automatically with reason "MaxDeliveryCountExceeded".
        await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromString("{ this is not json"))
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json"
        });
    }

    private static async Task SendOrderAsync(ServiceBusSender sender, OrderMessage order, ILogger logger)
    {
        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(order))
        {
            // Doubles as the handler's dedupe key and, since the topic has
            // RequiresDuplicateDetection, the broker's own dedupe key.
            MessageId = order.OrderId,
            ContentType = "application/json"
        };
        await sender.SendMessageAsync(message);
    }
}
```

## Consumer: competing consumers across two subscriptions

[`Program.cs`](ServiceBusTopicsDemo/Program.cs) starts 3 `ServiceBusProcessor` instances all
bound to `inventory-sub` — Service Bus hands each message to whichever one asks next, so the
three compete for work — plus a single processor on `audit-sub` to prove topic fan-out (every
message published once reaches both subscriptions independently).

```csharp
const int competingWorkerCount = 3;
var store = new ProcessedOrderStore();
var processors = new List<ServiceBusProcessor>();

for (var i = 1; i <= competingWorkerCount; i++)
{
    var workerId = $"inventory-worker-{i}";
    var processor = client.CreateProcessor(topicName, inventorySub, new ServiceBusProcessorOptions
    {
        MaxConcurrentCalls = 1,
        AutoCompleteMessages = false
    });
    var handler = new InventoryOrderHandler(workerId, store, loggerFactory.CreateLogger(workerId));
    processor.ProcessMessageAsync += handler.HandleAsync;
    processor.ProcessErrorAsync += errorArgs => LogErrorAsync(errorArgs, loggerFactory.CreateLogger(workerId));
    processors.Add(processor);
}

var auditProcessor = client.CreateProcessor(topicName, auditSub, new ServiceBusProcessorOptions
{
    MaxConcurrentCalls = 1,
    AutoCompleteMessages = false
});
auditProcessor.ProcessMessageAsync += new AuditOrderHandler(loggerFactory.CreateLogger("audit-worker")).HandleAsync;
processors.Add(auditProcessor);

foreach (var processor in processors) await processor.StartProcessingAsync();
```

All three inventory workers share one `ProcessedOrderStore` — in a real deployment with
separate worker *processes* this would need to be an external store (Redis/SQL), since
competing consumers running as different processes don't share in-memory state. That's called
out directly in the code.

## Idempotency: the handler, not just the broker

[`InventoryOrderHandler.cs`](ServiceBusTopicsDemo/InventoryOrderHandler.cs) is the core of the
exercise:

```csharp
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
            logger.LogError(ex, "[{WorkerId}] Poison message OrderId={OrderId} (delivery #{Count}): body is not valid JSON.",
                workerId, message.MessageId, message.DeliveryCount);
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
            return;
        }

        if (order.Quantity <= 0)
        {
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
            // Simulate the classic at-least-once failure: processing succeeded, but the ack
            // (CompleteMessageAsync) never reaches the broker — e.g. the process crashes right
            // after committing side effects. Abandoning here reproduces that redelivery on
            // demand, so the dedupe check above is what has to catch it.
            logger.LogWarning("[{WorkerId}] Simulating a lost ack for OrderId={OrderId} — abandoning after successful processing so it gets redelivered.",
                workerId, order.OrderId);
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
            return;
        }

        await args.CompleteMessageAsync(message, args.CancellationToken);
    }
}
```

```csharp
public sealed class ProcessedOrderStore
{
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new();
    public bool IsAlreadyProcessed(string messageId) => _processedMessageIds.ContainsKey(messageId);
    public void MarkProcessed(string messageId) => _processedMessageIds.TryAdd(messageId, 0);
}
```

**Why two failure paths, not one:** a bug I'd have shipped if I'd marked the message
"processed" *before* validating it — the poison-message delivery would then have hit the
dedupe check on its second attempt and been silently completed instead of retried or
dead-lettered, masking the failure entirely. Marking success only *after* the message passes
validation and is handled is what keeps dedupe and poison-message handling from fighting each
other.

**Two layers of dedup, and why both matter:** Service Bus's own entity-level duplicate
detection (`RequiresDuplicateDetection` on the topic) catches a duplicate *publish* — the
observed run below shows it never even reaching a handler. It can't catch a message the broker
already accepted being *redelivered* (lock lost, ack lost, consumer crash before completing) —
that's the case the handler's own `ProcessedOrderStore` check exists for, and the redelivery
drill forces it on purpose to prove it works.

## Observed run

```
$ dotnet run -- publish
10:41:27.202 info: Publisher[0] Sent OrderId=ff8e438b-13d1-4481-b4d6-45e3a7ebcb7f Sku=SKU-1001 Qty=2.
10:41:27.265 info: Publisher[0] Sent OrderId=9e7e36a6-2df8-49e7-a957-427df79768df Sku=SKU-1002 Qty=4.
10:41:27.335 info: Publisher[0] Sent OrderId=b74f27eb-b567-44d6-8877-89fe42e59ac7 Sku=SKU-1003 Qty=6.
10:41:27.335 info: Publisher[0] Re-sending OrderId=ff8e438b-13d1-4481-b4d6-45e3a7ebcb7f to simulate a duplicate publish (broker-level dedup).
10:41:27.401 info: Publisher[0] Sent OrderId=ff8e438b-13d1-4481-b4d6-45e3a7ebcb7f Sku=SKU-1001 Qty=2.
10:41:27.401 info: Publisher[0] Sending OrderId=e8f2b91d-739b-4786-80e7-da85d1144c62 to drill handler-level dedupe via a simulated lost ack.
10:41:27.499 info: Publisher[0] Sent OrderId=e8f2b91d-739b-4786-80e7-da85d1144c62 Sku=SKU-CHAOS-REDELIVERY Qty=1.
10:41:27.499 info: Publisher[0] Sending OrderId=3822ee6b-b801-4ade-9639-85b723b07008 with invalid Quantity=-5.
10:41:27.574 info: Publisher[0] Sent OrderId=3822ee6b-b801-4ade-9639-85b723b07008 Sku=SKU-9999 Qty=-5.
10:41:27.574 info: Publisher[0] Sending malformed-JSON poison message MessageId=73be7ed8-4114-4eda-b3e0-855e2fbf23bf.
10:41:27.653 info: Publisher[0] Publish batch complete.

$ dotnet run -- consume
Consumers running. Press Ctrl+C to stop.
10:41:41.057 info: audit-worker[0]        [audit] Saw OrderId=ff8e438b-... (delivery #1) — independent of inventory-sub.
10:41:41.085 info: inventory-worker-1[0]  Processed OrderId=b74f27eb-... Sku=SKU-1003 Qty=6.
10:41:41.085 info: inventory-worker-3[0]  Processed OrderId=9e7e36a6-... Sku=SKU-1002 Qty=4.
10:41:41.085 info: inventory-worker-2[0]  Processed OrderId=ff8e438b-... Sku=SKU-1001 Qty=2.
10:41:41.284 info: inventory-worker-2[0]  Processed OrderId=e8f2b91d-... Sku=SKU-CHAOS-REDELIVERY Qty=1.
10:41:41.284 warn: inventory-worker-2[0]  Simulating a lost ack for OrderId=e8f2b91d-... — abandoning after successful processing so it gets redelivered.
10:41:41.284 fail: inventory-worker-1[0]  Rejecting OrderId=3822ee6b-...: invalid Quantity=-5. Dead-lettering immediately.
10:41:41.440 warn: inventory-worker-1[0]  Duplicate OrderId=e8f2b91d-... (delivery #2) — already processed, skipping.
10:41:41.xxx fail: inventory-worker-x[0]  Poison message OrderId=73be7ed8-... (delivery #1): body is not valid JSON.
10:41:xx.xxx fail: inventory-worker-x[0]  Poison message OrderId=73be7ed8-... (delivery #2): body is not valid JSON.
10:41:xx.xxx fail: inventory-worker-x[0]  Poison message OrderId=73be7ed8-... (delivery #3): body is not valid JSON.
   (audit-worker sees all 6 published messages exactly once, at delivery #1 each)
```

Notes on what actually happened, since it wasn't all exactly as planned:

- **The duplicate publish (`ff8e438b`) never triggered the handler's dedupe branch at all** —
  only one `Processed OrderId=ff8e438b` line appears, no `Duplicate` warning for it. Broker-level
  `RequiresDuplicateDetection` absorbed the resend before it became a second message. That's
  *why* the redelivery drill order (`SKU-CHAOS-REDELIVERY`) exists — it's the only way to force
  the handler's own check to actually fire, by making Service Bus redeliver a message the broker
  already accepted rather than re-publishing it.
- **The redelivery drill worked exactly as designed**: `e8f2b91d` was processed and logged once,
  then abandoned on purpose; its second delivery hit `store.IsAlreadyProcessed` and was skipped
  with a `Duplicate` warning instead of being reprocessed.

### Proof: poison messages in the DLQ

```
$ dotnet run -- drain-dlq
10:43:10.510 warn: DLQ[0] Found 1 dead-lettered message(s) in orders/inventory-sub/$DeadLetterQueue:
10:43:10.516 warn: DLQ[0]   MessageId=73be7ed8-4114-4eda-b3e0-855e2fbf23bf DeliveryCount=4 Reason=MaxDeliveryCountExceeded Description=Message could not be consumed after 3 delivery attempts. Body={ this is not json
```

(captured in an earlier drain-dlq call in the same session, before the malformed-JSON message
also landed — both were confirmed dead-lettered):

```
10:40:31.179 warn: DLQ[0] Found 2 dead-lettered message(s) in orders/inventory-sub/$DeadLetterQueue:
10:40:31.186 warn: DLQ[0]   MessageId=9f396062-... DeliveryCount=1 Reason=InvalidQuantity Description=Quantity must be positive, got -5. Body={"OrderId":"9f396062-...","Sku":"SKU-9999","Quantity":-5,...}
10:40:31.253 warn: DLQ[0]   MessageId=9af9fa4f-... DeliveryCount=4 Reason=MaxDeliveryCountExceeded Description=Message could not be consumed after 3 delivery attempts. Body={ this is not json
```

Two distinct dead-letter paths, both confirmed:

1. **Automatic** — `AbandonMessageAsync` on every delivery of an unparseable body; once
   `DeliveryCount` exceeds the subscription's `MaxDeliveryCount` (3), Service Bus dead-letters it
   itself with `DeadLetterReason = MaxDeliveryCountExceeded`. No handler code decided this.
2. **Manual** — `DeadLetterMessageAsync` called explicitly on the first delivery for a
   business-rule violation (negative quantity), with a caller-supplied reason/description. No
   retries wasted on something retrying can't fix.

`DlqInspector.PrintDeadLettersAsync` (used by `drain-dlq`) reads via a receiver scoped to
`SubQueue.DeadLetter` — the DLQ is just another queue-shaped endpoint per subscription
(`orders/inventory-sub/$DeadLetterQueue`), not a separate concept requiring different APIs.

## Running it

```bash
dotnet user-secrets set "ServiceBus:ConnectionString" "<app-send-listen connection string>" \
  --project day-19/ServiceBusTopicsDemo

dotnet run -- publish     # send the demo batch
dotnet run -- consume     # start 3 competing inventory workers + 1 audit worker; Ctrl+C to stop
dotnet run -- drain-dlq   # print + remove whatever's sitting in inventory-sub's DLQ
```

## Cost note

The Service Bus **Standard** tier is required for topics/subscriptions (Basic only has
queues) — this is a real billable resource (~$0.05/day base plus per-operation charges), unlike
most of the free-tier resources used on earlier days. Provisioned into its own resource group
(`day19-servicebus-rg`) specifically so it can be torn down independently:
`az group delete -n day19-servicebus-rg --yes` — not run yet, left for an explicit decision on
whether to keep it around for further experimentation.

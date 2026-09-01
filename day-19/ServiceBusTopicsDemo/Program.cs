using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceBusTopicsDemo;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var topicName = config["ServiceBus:TopicName"]!;
var inventorySub = config["ServiceBus:InventorySubscription"]!;
var auditSub = config["ServiceBus:AuditSubscription"]!;

// The connection string for the scoped Send+Listen policy ("app-send-listen") is never
// committed: it's supplied via `dotnet user-secrets set ServiceBus:ConnectionString "..."`
// (or the SERVICEBUS_CONNECTIONSTRING env var) — see day-19 notes for the exact commands.
// RBAC (DefaultAzureCredential + a data-plane role assignment) isn't available on this
// subscription, which is why this falls back to a SAS connection string instead.
var connectionString = config["ServiceBus:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Missing ServiceBus:ConnectionString. Set it with `dotnet user-secrets set ServiceBus:ConnectionString \"<value>\"` " +
        "or the SERVICEBUS_CONNECTIONSTRING environment variable.");

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss.fff ";
    })
    .SetMinimumLevel(LogLevel.Information));

await using var client = new ServiceBusClient(connectionString);

var mode = args.Length > 0 ? args[0] : "consume";

switch (mode)
{
    case "publish":
        await Publisher.SendDemoBatchAsync(client, topicName, loggerFactory.CreateLogger("Publisher"));
        break;

    case "consume":
        await ConsumeAsync(client, topicName, inventorySub, auditSub, loggerFactory);
        break;

    case "drain-dlq":
        await DlqInspector.PrintDeadLettersAsync(client, topicName, inventorySub, loggerFactory.CreateLogger("DLQ"));
        break;

    default:
        Console.WriteLine("Usage: dotnet run -- [publish|consume|drain-dlq]");
        break;
}

static async Task ConsumeAsync(ServiceBusClient client, string topicName, string inventorySub, string auditSub, ILoggerFactory loggerFactory)
{
    const int competingWorkerCount = 3;
    var store = new ProcessedOrderStore();
    var processors = new List<ServiceBusProcessor>();

    // Competing consumers: N processor instances pulling from the SAME subscription. Service Bus
    // hands each message to exactly one of them (whichever asks next), so load is spread across
    // workers instead of every worker seeing every message.
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

    // A single consumer on the second subscription, just to prove the topic fans the same
    // publish out to both subscriptions independently.
    var auditProcessor = client.CreateProcessor(topicName, auditSub, new ServiceBusProcessorOptions
    {
        MaxConcurrentCalls = 1,
        AutoCompleteMessages = false
    });
    var auditHandler = new AuditOrderHandler(loggerFactory.CreateLogger("audit-worker"));
    auditProcessor.ProcessMessageAsync += auditHandler.HandleAsync;
    auditProcessor.ProcessErrorAsync += errorArgs => LogErrorAsync(errorArgs, loggerFactory.CreateLogger("audit-worker"));
    processors.Add(auditProcessor);

    foreach (var processor in processors)
    {
        await processor.StartProcessingAsync();
    }

    Console.WriteLine("Consumers running. Press Ctrl+C to stop.");
    var stopSignal = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stopSignal.TrySetResult();
    };
    await stopSignal.Task;

    foreach (var processor in processors)
    {
        await processor.StopProcessingAsync();
        await processor.DisposeAsync();
    }
}

static Task LogErrorAsync(ProcessErrorEventArgs args, ILogger logger)
{
    logger.LogError(args.Exception, "Processor error in {Source}", args.ErrorSource);
    return Task.CompletedTask;
}

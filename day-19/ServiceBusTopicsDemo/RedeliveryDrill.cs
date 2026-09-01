namespace ServiceBusTopicsDemo;

/// <summary>
/// Marker used by the publisher and the inventory handler to stage an on-demand redelivery
/// (see InventoryOrderHandler) — proving the handler's own idempotency check, independent of
/// Service Bus's entity-level duplicate detection on a re-published message.
/// </summary>
public static class RedeliveryDrill
{
    public const string MarkerSku = "SKU-CHAOS-REDELIVERY";
}

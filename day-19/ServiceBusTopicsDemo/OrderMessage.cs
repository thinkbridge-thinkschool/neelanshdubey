namespace ServiceBusTopicsDemo;

public sealed record OrderMessage(string OrderId, string Sku, int Quantity, DateTimeOffset PlacedAtUtc);

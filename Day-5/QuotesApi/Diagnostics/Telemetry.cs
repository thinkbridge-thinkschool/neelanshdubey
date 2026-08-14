using System.Diagnostics;

namespace QuotesApi.Diagnostics;

public static class Telemetry
{
    public static readonly ActivitySource Source = new("QuotesApi");
}

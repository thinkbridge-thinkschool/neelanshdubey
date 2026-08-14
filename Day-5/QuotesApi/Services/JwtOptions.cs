namespace QuotesApi.Services;

public record JwtOptions
{
    public required string Audience { get; init; }
    public required TimeSpan AccessTokenLifetime { get; init; }
}

using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Models;

public class RefreshToken
{
    public int Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public int UserId { get; set; }

    public User? User { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    [ConcurrencyCheck]
    public DateTimeOffset? RevokedAt { get; set; }

    public string? ReplacedByToken { get; set; }

    public string FamilyId { get; set; } = string.Empty;

    public bool IsRevoked => RevokedAt.HasValue;
}

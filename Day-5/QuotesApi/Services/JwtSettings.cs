namespace QuotesApi.Services;

// Issuer/SigningKey are bound once at startup for TokenValidationParameters —
// they aren't part of the JwtOptions/IOptions pattern because signing
// material shouldn't hot-reload. SigningKey is never set in appsettings.json:
//   - Local dev: dotnet user-secrets set "Jwt:SigningKey" "<value>" (this project)
//   - Production: supplied via an env var (Jwt__SigningKey) sourced from a
//     Key Vault reference, injected by the hosting platform.
public class JwtSettings
{
    public string? Issuer { get; set; }
    public string? SigningKey { get; set; }
}

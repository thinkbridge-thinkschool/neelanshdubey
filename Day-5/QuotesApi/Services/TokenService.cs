using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Models;

namespace QuotesApi.Services;

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly IOptions<JwtOptions> _jwtOptions;

    // TokenService is a singleton, so IOptions<JwtOptions> is resolved once
    // and cached for the app's lifetime — a config reload won't be picked up
    // without a restart. Contrast with the IOptionsSnapshot<JwtOptions> usage
    // in AuthEndpointExtensions, which re-reads per request.
    public TokenService(JwtSettings jwtSettings, IOptions<JwtOptions> jwtOptions)
    {
        _jwtSettings = jwtSettings;
        _jwtOptions = jwtOptions;
    }

    public int RefreshTokenValidityInDays => 7;

    public string CreateAccessToken(User user)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_jwtSettings.SigningKey!);
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("scope", "quotes.write")
        };

        var expires = DateTime.UtcNow.Add(_jwtOptions.Value.AccessTokenLifetime);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtOptions.Value.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken()
    {
        var randomBytes = new byte[64];
        RandomNumberGenerator.Fill(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public string HashRefreshToken(string refreshToken)
    {
        using var sha = SHA256.Create();
        var tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = sha.ComputeHash(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }
}

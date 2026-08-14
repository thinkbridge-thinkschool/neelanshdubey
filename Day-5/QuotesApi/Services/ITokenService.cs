using QuotesApi.Models;

namespace QuotesApi.Services;

public interface ITokenService
{
    string CreateAccessToken(User user);

    string CreateRefreshToken();

    string HashRefreshToken(string refreshToken);

    int RefreshTokenValidityInDays { get; }
}

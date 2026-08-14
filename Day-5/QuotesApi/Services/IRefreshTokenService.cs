using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IRefreshTokenService
{
    Task<string> CreateRefreshTokenAsync(User user);

    Task<TokenResponse?> RefreshAsync(string refreshToken);

    Task<bool> RevokeAsync(string refreshToken);
}

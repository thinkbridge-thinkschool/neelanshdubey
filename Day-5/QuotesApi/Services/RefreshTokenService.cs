using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;

    public RefreshTokenService(
        AppDbContext dbContext,
        ITokenService tokenService)
        : this(dbContext, tokenService, new SystemClock())
    {
    }

    public RefreshTokenService(
        AppDbContext dbContext,
        ITokenService tokenService,
        IClock clock)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _clock = clock;
    }

    public async Task<string> CreateRefreshTokenAsync(User user)
    {
        var rawToken = _tokenService.CreateRefreshToken();
        var hashedToken = _tokenService.HashRefreshToken(rawToken);
        var familyId = Guid.NewGuid().ToString();

        var refreshToken = new RefreshToken
        {
            Token = hashedToken,
            UserId = user.Id,
            ExpiresAt = _clock.UtcNow.AddDays(_tokenService.RefreshTokenValidityInDays),
            FamilyId = familyId
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return rawToken;
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken)
    {
        var hashedToken = _tokenService.HashRefreshToken(refreshToken);
        var tokenEntity = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .SingleOrDefaultAsync(rt => rt.Token == hashedToken);

        if (tokenEntity is null)
            return null;

        if (tokenEntity.ExpiresAt <= _clock.UtcNow)
            return null;

        if (tokenEntity.IsRevoked)
        {
            if (!string.IsNullOrEmpty(tokenEntity.ReplacedByToken))
            {
                await RevokeFamilyAsync(tokenEntity.FamilyId);
            }

            return null;
        }

        var user = tokenEntity.User;
        if (user is null)
            return null;

        tokenEntity.RevokedAt = _clock.UtcNow;

        var newRawToken = _tokenService.CreateRefreshToken();
        var newHashedToken = _tokenService.HashRefreshToken(newRawToken);

        tokenEntity.ReplacedByToken = newHashedToken;

        var newRefreshToken = new RefreshToken
        {
            Token = newHashedToken,
            UserId = user.Id,
            ExpiresAt = _clock.UtcNow.AddDays(_tokenService.RefreshTokenValidityInDays),
            FamilyId = tokenEntity.FamilyId
        };

        _dbContext.RefreshTokens.Add(newRefreshToken);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent request already rotated this token between our read
            // and this write (RevokedAt is a concurrency token) — treat this request
            // the same as any other reuse of an already-rotated token.
            return null;
        }

        return new TokenResponse
        {
            AccessToken = _tokenService.CreateAccessToken(user),
            RefreshToken = newRawToken
        };
    }

    public async Task<bool> RevokeAsync(string refreshToken)
    {
        var hashedToken = _tokenService.HashRefreshToken(refreshToken);
        var tokenEntity = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.Token == hashedToken);

        if (tokenEntity is null)
            return false;

        if (tokenEntity.IsRevoked)
            return false;

        tokenEntity.RevokedAt = _clock.UtcNow;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    private async Task RevokeFamilyAsync(string familyId)
    {
        var familyTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var token in familyTokens)
        {
            token.RevokedAt = _clock.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
    }
}

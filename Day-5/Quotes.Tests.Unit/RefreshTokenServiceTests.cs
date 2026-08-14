using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MockQueryable.NSubstitute;
using NSubstitute;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public class RefreshTokenServiceTests
{
    private static AppDbContext CreateDbContext(List<RefreshToken> tokens)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().Options;
        var dbContext = Substitute.For<AppDbContext>(options);

        var mockSet = tokens.BuildMockDbSet();
        mockSet.When(s => s.Add(Arg.Any<RefreshToken>()))
            .Do(call => tokens.Add(call.Arg<RefreshToken>()));

        dbContext.Set<RefreshToken>().Returns(mockSet);
        dbContext.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

        return dbContext;
    }

    private static ITokenService CreateTokenService(
        string rawRefreshToken = "new-raw-refresh-token",
        string accessToken = "new-access-token",
        int refreshTokenValidityInDays = 7)
    {
        var tokenService = Substitute.For<ITokenService>();
        tokenService.RefreshTokenValidityInDays.Returns(refreshTokenValidityInDays);
        tokenService.CreateRefreshToken().Returns(rawRefreshToken);
        tokenService.HashRefreshToken(Arg.Any<string>())
            .Returns(callInfo => "hashed:" + callInfo.Arg<string>());
        tokenService.CreateAccessToken(Arg.Any<User>()).Returns(accessToken);
        return tokenService;
    }

    [Fact]
    public async Task CreateRefreshTokenAsync_ValidUser_ReturnsRawTokenAndPersistsHashedToken()
    {
        // Arrange
        var tokens = new List<RefreshToken>();
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService(rawRefreshToken: "raw-token-123", refreshTokenValidityInDays: 7);
        var service = new RefreshTokenService(dbContext, tokenService);
        var user = new User { Id = 5, Email = "reader@example.com", PasswordHash = "hash" };
        var before = DateTimeOffset.UtcNow;

        // Act
        var result = await service.CreateRefreshTokenAsync(user);

        // Assert
        result.Should().Be("raw-token-123");
        tokens.Should().ContainSingle();
        var persisted = tokens.Single();
        persisted.Token.Should().Be("hashed:raw-token-123");
        persisted.UserId.Should().Be(5);
        persisted.ExpiresAt.Should().BeCloseTo(before.AddDays(7), TimeSpan.FromSeconds(5));
        persisted.FamilyId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshAsync_ActiveToken_RotatesTokenAndReturnsNewTokenPair()
    {
        // Arrange
        var user = new User { Id = 9, Email = "reader@example.com", PasswordHash = "hash" };
        var existingToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:raw-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            FamilyId = "family-1"
        };
        var tokens = new List<RefreshToken> { existingToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService(rawRefreshToken: "new-raw-token", accessToken: "new-access-token");
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var response = await service.RefreshAsync("raw-token");

        // Assert
        response.Should().NotBeNull();
        response!.AccessToken.Should().Be("new-access-token");
        response.RefreshToken.Should().Be("new-raw-token");

        existingToken.RevokedAt.Should().NotBeNull();
        existingToken.ReplacedByToken.Should().Be("hashed:new-raw-token");

        tokens.Should().HaveCount(2);
        var newToken = tokens.Single(t => t.Token == "hashed:new-raw-token");
        newToken.FamilyId.Should().Be("family-1");
        newToken.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task RefreshAsync_TokenNotFound_ReturnsNull()
    {
        // Arrange
        var tokens = new List<RefreshToken>();
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var response = await service.RefreshAsync("unknown-raw-token");

        // Assert
        response.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNullWithoutRotating()
    {
        // Arrange
        var user = new User { Id = 3, Email = "reader@example.com", PasswordHash = "hash" };
        var expiredToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:expired-raw-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            FamilyId = "family-1"
        };
        var tokens = new List<RefreshToken> { expiredToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var response = await service.RefreshAsync("expired-raw-token");

        // Assert
        response.Should().BeNull();
        expiredToken.RevokedAt.Should().BeNull();
        tokens.Should().ContainSingle();
    }

    [Fact]
    public async Task RefreshAsync_RevokedTokenWithoutReplacement_ReturnsNullAndDoesNotTouchFamily()
    {
        // Arrange
        var user = new User { Id = 4, Email = "reader@example.com", PasswordHash = "hash" };
        var revokedToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:revoked-raw-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReplacedByToken = null,
            FamilyId = "family-1"
        };
        var siblingActiveToken = new RefreshToken
        {
            Id = 2,
            Token = "hashed:sibling-raw-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            FamilyId = "family-1"
        };
        var tokens = new List<RefreshToken> { revokedToken, siblingActiveToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var response = await service.RefreshAsync("revoked-raw-token");

        // Assert
        response.Should().BeNull();
        siblingActiveToken.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ReuseOfAlreadyRotatedToken_RevokesEntireTokenFamily()
    {
        // Arrange
        var user = new User { Id = 6, Email = "reader@example.com", PasswordHash = "hash" };
        var reusedToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:reused-raw-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ReplacedByToken = "hashed:already-issued-successor",
            FamilyId = "family-1"
        };
        var activeDescendantToken = new RefreshToken
        {
            Id = 2,
            Token = "hashed:already-issued-successor",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            FamilyId = "family-1"
        };
        var unrelatedFamilyToken = new RefreshToken
        {
            Id = 3,
            Token = "hashed:unrelated-raw-token",
            UserId = user.Id,
            User = user,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            FamilyId = "family-2"
        };
        var tokens = new List<RefreshToken> { reusedToken, activeDescendantToken, unrelatedFamilyToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var response = await service.RefreshAsync("reused-raw-token");

        // Assert
        response.Should().BeNull();
        activeDescendantToken.RevokedAt.Should().NotBeNull();
        unrelatedFamilyToken.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_TokenWithoutAssociatedUser_ReturnsNullWithoutRotating()
    {
        // Arrange: a token row that has outlived its user (e.g. the user was
        // deleted out-of-band) but is itself still active and unrevoked.
        var orphanedToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:orphaned-raw-token",
            UserId = 999,
            User = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            FamilyId = "family-1"
        };
        var tokens = new List<RefreshToken> { orphanedToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var response = await service.RefreshAsync("orphaned-raw-token");

        // Assert
        response.Should().BeNull();
        orphanedToken.RevokedAt.Should().BeNull();
        tokens.Should().ContainSingle();
    }

    [Fact]
    public async Task RevokeAsync_ActiveToken_RevokesAndReturnsTrue()
    {
        // Arrange
        var activeToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:active-raw-token",
            UserId = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            FamilyId = "family-1"
        };
        var tokens = new List<RefreshToken> { activeToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var result = await service.RevokeAsync("active-raw-token");

        // Assert
        result.Should().BeTrue();
        activeToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeAsync_TokenNotFound_ReturnsFalse()
    {
        // Arrange
        var tokens = new List<RefreshToken>();
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var result = await service.RevokeAsync("unknown-raw-token");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevokedToken_ReturnsFalseAndLeavesRevokedAtUnchanged()
    {
        // Arrange
        var revokedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var alreadyRevokedToken = new RefreshToken
        {
            Id = 1,
            Token = "hashed:revoked-raw-token",
            UserId = 1,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = revokedAt,
            FamilyId = "family-1"
        };
        var tokens = new List<RefreshToken> { alreadyRevokedToken };
        var dbContext = CreateDbContext(tokens);
        var tokenService = CreateTokenService();
        var service = new RefreshTokenService(dbContext, tokenService);

        // Act
        var result = await service.RevokeAsync("revoked-raw-token");

        // Assert
        result.Should().BeFalse();
        alreadyRevokedToken.RevokedAt.Should().Be(revokedAt);
    }
}

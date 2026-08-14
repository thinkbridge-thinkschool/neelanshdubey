using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Diagnostics;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            AppDbContext dbContext,
            ITokenService tokenService,
            IRefreshTokenService refreshTokenService,
            IOptionsSnapshot<JwtOptions> jwtOptions,
            ILogger<Program> logger) =>
        {
            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.Email == request.Email);

            if (user is null || !VerifyPassword(user, request.Password))
            {
                logger.LogWarning("Login failed for email {Email}", request.Email);
                return Results.Unauthorized();
            }

            var accessToken = tokenService.CreateAccessToken(user);
            var refreshToken = await refreshTokenService.CreateRefreshTokenAsync(user);

            // IOptionsSnapshot<JwtOptions> is resolved fresh from this request's
            // DI scope, unlike TokenService's IOptions<JwtOptions> singleton above.
            logger.LogInformation(
                "Login succeeded for user {UserId}; issuing token for audience {Audience} (ttl {Lifetime})",
                user.Id, jwtOptions.Value.Audience, jwtOptions.Value.AccessTokenLifetime);

            return Results.Ok(new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            });
        });

        app.MapPost("/api/auth/refresh", async (
            RefreshRequest request,
            IRefreshTokenService refreshTokenService,
            ILogger<Program> logger) =>
        {
            var response = await refreshTokenService.RefreshAsync(request.RefreshToken);

            if (response is null)
            {
                logger.LogWarning("Refresh token rotation failed: token invalid, expired, or reused");
                return Results.Unauthorized();
            }

            logger.LogInformation("Refresh token rotated successfully");
            return Results.Ok(response);
        });

        app.MapPost("/api/auth/logout", async (
            LogoutRequest request,
            IRefreshTokenService refreshTokenService,
            ILogger<Program> logger) =>
        {
            var revoked = await refreshTokenService.RevokeAsync(request.RefreshToken);

            if (!revoked)
            {
                logger.LogWarning("Logout failed: refresh token invalid or already revoked");
                return Results.Unauthorized();
            }

            logger.LogInformation("Logout succeeded, refresh token revoked");
            return Results.NoContent();
        });

        return app;
    }

    private static bool VerifyPassword(User user, string password)
    {
        using var activity = Telemetry.Source.StartActivity("password-verification");
        activity?.SetTag("user.id", user.Id);

        return BCryptNet.BCrypt.Verify(password, user.PasswordHash);
    }
}

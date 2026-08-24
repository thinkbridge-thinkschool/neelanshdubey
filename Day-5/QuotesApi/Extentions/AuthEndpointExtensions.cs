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

        app.MapPost("/api/auth/register", async (
            RegisterRequest request,
            AppDbContext dbContext,
            ITokenService tokenService,
            IRefreshTokenService refreshTokenService,
            ILogger<Program> logger) =>
        {
            var email = request.Email.Trim();
            var password = request.Password;

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["email"] = ["A valid email is required."]
                });
            }

            if (password.Length < 8)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = ["Password must be at least 8 characters."]
                });
            }

            var alreadyExists = await dbContext.Users.AnyAsync(u => u.Email == email);

            if (alreadyExists)
            {
                logger.LogWarning("Registration failed: {Email} is already registered", email);
                return Results.Conflict(new { message = "An account with that email already exists." });
            }

            var user = new User
            {
                Email = email,
                PasswordHash = BCryptNet.BCrypt.HashPassword(password)
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            var accessToken = tokenService.CreateAccessToken(user);
            var refreshToken = await refreshTokenService.CreateRefreshTokenAsync(user);

            logger.LogInformation("Registered new user {UserId}", user.Id);

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

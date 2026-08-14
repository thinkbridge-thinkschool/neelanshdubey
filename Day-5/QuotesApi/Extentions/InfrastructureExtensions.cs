using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration
            .GetSection("Jwt")
            .Get<JwtSettings>()
            ?? throw new InvalidOperationException("Jwt settings are missing.");

        var jwtOptions = configuration
            .GetSection("Jwt")
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt settings are missing.");

        var entraSettings = configuration
            .GetSection("Entra")
            .Get<EntraSettings>()
            ?? throw new InvalidOperationException("Entra settings are missing.");

        var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.SigningKey!);

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(
              configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<IQuoteValidator, QuoteValidator>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.AddSingleton(jwtSettings);
        services.AddSingleton(entraSettings);

        // Typed options for consumers that resolve JwtOptions via DI
        // (IOptions<T> / IOptionsSnapshot<T>) — see TokenService and
        // AuthEndpointExtensions for the two usages.
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Bearer";
            options.DefaultChallengeScheme = "Bearer";
        })
        .AddPolicyScheme("Bearer", "InternalJwt or EntraJwt", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authHeader = context.Request.Headers.Authorization.ToString();

                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader["Bearer ".Length..].Trim();
                    var handler = new JwtSecurityTokenHandler();

                    if (handler.CanReadToken(token))
                    {
                        var issuer = handler.ReadJwtToken(token).Issuer;

                        if (issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
                        {
                            return "EntraJwt";
                        }
                    }
                }

                return "InternalJwt";
            };
        })
        .AddJwtBearer("InternalJwt", options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,

                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtOptions.Audience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(keyBytes),

                ClockSkew = TimeSpan.Zero
            };
        })
        .AddJwtBearer("EntraJwt", options =>
        {
            options.Authority =
                $"https://login.microsoftonline.com/{entraSettings.TenantId}/v2.0";

            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,

                    ValidAudience = entraSettings.Audience,

                    ClockSkew = TimeSpan.Zero
                };
        });

        services.AddAuthorization();

        return services;
    }

    public static void ApplyMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Database.Migrate();
        SeedUsers(dbContext);
    }

    private static void SeedUsers(AppDbContext dbContext)
    {
        if (dbContext.Users.Any())
            return;

        dbContext.Users.Add(new User
        {
            Email = UserSeed.Email,
            PasswordHash = BCryptNet.BCrypt.HashPassword(UserSeed.Password)
        });

        try
        {
            dbContext.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Another instance seeded the same user concurrently; the unique
            // index on Email already guarantees at most one row exists.
        }
    }
}
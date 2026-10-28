using BackgroundJobs.Core.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BackgroundJobs.Api.Features.Auth;

internal static class AuthSetup
{
    public const string RoleClaim = "role";

    /// <summary>Start, cancel and retry jobs; manage users.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>Read job status, results and statistics. Admins pass too.</summary>
    public const string ViewerPolicy = "Viewer";

    public static IServiceCollection AddJobsAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 24 * 60, "Jwt:AccessTokenMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<PasswordVerifier>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyProvider, IOptions<JwtOptions>>((bearer, keys, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = keys.Key,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = RoleClaim,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(ViewerPolicy, policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Viewer)));

        return services;
    }
}

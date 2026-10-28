using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BackgroundJobs.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user. Endpoints that call this require authorization, so the claim is always present.</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The token has no subject claim."));

    /// <summary>The user id when signed in with either scheme, otherwise <c>null</c>.</summary>
    public static Guid? UserIdOrNull(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}

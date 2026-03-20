using System.Security.Claims;

namespace Lyra.Core;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Safely extracts the internal Lyra User ID from the current user's claims.
    /// Returns null if the claim is missing or not a valid GUID.
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var claimValue = user.FindFirst(LyraClaimTypes.UserId)?.Value;

        if (Guid.TryParse(claimValue, out var lyraId))
        {
            return lyraId;
        }

        return null;
    }
}
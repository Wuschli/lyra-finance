using System.Security.Claims;
using Lyra.Core.Services;
using Microsoft.AspNetCore.Authentication;

namespace Lyra.Core.Auth;

public class UserClaimsTransformation : IClaimsTransformation
{
    private readonly UserService _userService;

    public UserClaimsTransformation(UserService userService)
    {
        _userService = userService;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // check if user has already been transformed
        if (principal.HasClaim(c => c.Type == LyraClaimTypes.UserId))
            return principal;

        var zitadelId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(zitadelId))
            return principal;

        var userId = await _userService.EnsureUserExists(zitadelId);

        var clone = principal.Clone();
        var newIdentity = (ClaimsIdentity)clone.Identity!;

        newIdentity.AddClaim(new Claim(LyraClaimTypes.UserId, userId.ToString()));

        return clone;
    }
}
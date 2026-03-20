using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Lyra.Core;

public class UserClaimsTransformation : IClaimsTransformation
{
    private readonly IUserService _userService;

    public UserClaimsTransformation(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // check if user has already been transformed
        if (principal.HasClaim(c => c.Type == "lyra_user_id"))
            return principal;

        var zitadelId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(zitadelId))
            return principal;

        var userId = await _userService.EnsureUserExists(zitadelId);
        
        var clone = principal.Clone();
        var newIdentity = (ClaimsIdentity)clone.Identity!;

        newIdentity.AddClaim(new Claim("lyra_user_id", userId.ToString()));

        return clone;
    }
}
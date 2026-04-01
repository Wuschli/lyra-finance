using Dapper;
using Lyra.Core.Extensions;
using Microsoft.AspNetCore.Components.Authorization;

namespace Lyra.Core.Services;

public class UserService
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly AuthenticationStateProvider _authStateProvider;

    public UserService(IDbConnectionFactory dbFactory, AuthenticationStateProvider authStateProvider)
    {
        _dbFactory = dbFactory;
        _authStateProvider = authStateProvider;
    }

    public async Task<Guid> EnsureUserExists(string zitadelId)
    {
        using var db = await _dbFactory.CreateConnectionAsync();

        const string sql = @"
            INSERT INTO users (zitadel_id)
            VALUES (@ZitadelId)
            ON CONFLICT (zitadel_id)
            DO UPDATE SET last_login = CURRENT_TIMESTAMP
            RETURNING id;";

        var userId = await db.ExecuteScalarAsync<Guid>(sql, new { zitadelId });
        return userId;
    }

    public async Task<Guid> GetCurrentUserId()
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.GetUserId();

        if (userId is null)
            throw new InvalidOperationException("The current user does not have a Lyra user ID claim. Ensure the user is authenticated and claims have been transformed.");

        return userId.Value;
    }
}
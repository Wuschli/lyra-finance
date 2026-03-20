using Dapper;
using Lyra.Core.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace Lyra.Core.Services;

public class AccountService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AuthenticationStateProvider _authStateProvider;

    public AccountService(IDbConnectionFactory connectionFactory, AuthenticationStateProvider authStateProvider)
    {
        _connectionFactory = connectionFactory;
        _authStateProvider = authStateProvider;
    }

    public async Task<IEnumerable<Account>> GetAccountsForCurrentUserAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        var userId = user.GetUserId();

        if (userId == null)
        {
            return Enumerable.Empty<Account>();
        }

        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT id, user_id, name, created_at
            FROM accounts
            WHERE user_id = @UserId
            ORDER BY name ASC;";

        return await connection.QueryAsync<Account>(sql, new { UserId = userId });
    }

    public async Task CreateAccountForCurrentUserAsync(string accountName)
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.GetUserId();

        if (userId == null) throw new UnauthorizedAccessException("User not identified.");

        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
        INSERT INTO lyra.accounts (user_id, name)
        VALUES (@UserId, @Name);";

        await connection.ExecuteAsync(sql, new { UserId = userId.Value, Name = accountName });
    }
}
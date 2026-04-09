using Dapper;
using Lyra.Core.Extensions;
using Lyra.Core.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace Lyra.Core.Services;

public class AccountService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly AccountNotificationService _accountNotificationService;

    public AccountService(
        IDbConnectionFactory connectionFactory,
        AuthenticationStateProvider authStateProvider,
        AccountNotificationService accountNotificationService)
    {
        _connectionFactory = connectionFactory;
        _authStateProvider = authStateProvider;
        _accountNotificationService = accountNotificationService;
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
            SELECT id, user_id, name, created_at, current_balance, current_balance_at
            FROM accounts
            WHERE user_id = @UserId
            ORDER BY name ASC;";

        return await connection.QueryAsync<Account>(sql, new { UserId = userId });
    }

    public async Task<Guid> CreateAccountForCurrentUserAsync(string accountName)
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.GetUserId();

        if (userId == null) throw new UnauthorizedAccessException("User not identified.");

        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            INSERT INTO lyra.accounts (user_id, name)
            VALUES (@UserId, @Name)
            RETURNING id;";

        return await connection.ExecuteScalarAsync<Guid>(sql, new { UserId = userId.Value, Name = accountName });
    }

    public async Task RenameAccountAsync(Guid accountId, string newName)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            UPDATE lyra.accounts
            SET name = @Name
            WHERE id = @Id;";

        await connection.ExecuteAsync(sql, new { Id = accountId, Name = newName });
    }

    public async Task UpdateBalanceAsync(Guid accountId, decimal balance, DateTimeOffset balanceAt)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            UPDATE lyra.accounts
            SET current_balance = @Balance,
                current_balance_at = @BalanceAt
            WHERE id = @Id;";

        await connection.ExecuteAsync(sql, new { Id = accountId, Balance = balance, BalanceAt = balanceAt });
        await _accountNotificationService.NotifyAccountsChangedAsync();
    }
}
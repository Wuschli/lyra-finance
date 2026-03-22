using Dapper;
using Lyra.Core.EnableBanking.Models;
using Lyra.Core.EnableBanking;
using Lyra.Core.Models;

namespace Lyra.Core.Services;

public class EnableBankingService
{
    private readonly ApiClient _client;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly UserService _userService;

    public EnableBankingService(ApiClient client, IDbConnectionFactory connectionFactory, UserService userService)
    {
        _client = client;
        _connectionFactory = connectionFactory;
        _userService = userService;
    }


    /// <summary>
    /// Starts the user authorization flow (POST /sessions).
    /// </summary>
    public async Task<StartAuthorizationResponse?> StartAuthorizationAsync(Guid externalConnectionId, Guid userId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
        INSERT INTO external_connections 
            (id, user_id, provider_name, created_at)
        VALUES 
            (@Id, @UserId, 'enable_banking', @Now)
        ON CONFLICT (id)
        DO NOTHING;";

        await connection.ExecuteAsync(sql, new
        {
            Id = externalConnectionId,
            UserId = userId,
            DateTimeOffset.Now
        });

        var authRequest = new StartAuthorizationRequest
        {
            Access = new Access { ValidUntil = DateTimeOffset.UtcNow.AddDays(90) },
            Aspsp = new ASPSP { Name = "Sparkasse Hildesheim Goslar Peine", Country = "DE" }, // TODO as parameter
            State = externalConnectionId.ToString(),
            RedirectUrl = "https://localhost:7001/enable_banking/callback", // TODO base url from config
            PsuType = PSUType.Personal
        };

        var authResponse = await _client.Auth.PostAsync(authRequest);
        return authResponse;
    }

    // http://localhost:7001/enable_banking/callback?state=9588ff43-b08a-4fc0-ab5f-6fc17bd8e7bf&code=d8a3994a-a693-494b-a58f-62c5fc75929b
    public async Task FinalizeConnectionAsync(Guid externalConnectionId, string code)
    {
        var sessionRequest = new AuthorizeSessionRequest
        {
            Code = code
        };
        var sessionResponse = await _client.Sessions.PostAsync(sessionRequest);
        if (sessionResponse == null)
            throw new Exception(); // TODO
        await UpdateSession(externalConnectionId, sessionResponse.SessionId!, sessionResponse.Access!.ValidUntil!.Value);
        await UpsertExternalAccounts(externalConnectionId, sessionResponse.Accounts!);
    }

    private async Task UpdateSession(Guid externalConnectionId, string sessionId, DateTimeOffset validUntil)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
        UPDATE external_connections
        SET session_id = @SessionId,
            expires_at = @ValidUntil
        WHERE id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            Id = externalConnectionId,
            SessionId = sessionId,
            ValidUntil = validUntil
        });
    }


    private async Task UpsertExternalAccounts(Guid externalConnectionId, List<AccountResource> accounts)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
        INSERT INTO enable_banking_accounts
            (external_connection_id, identification_hash, name, details, iban, currency, account_type, product_name)
        VALUES
            (@ConnectionId, @Hash, @Name, @Details, @Iban, @Currency, @AccountType, @ProductName)
        ON CONFLICT (external_connection_id, identification_hash)
        DO UPDATE SET
            name = EXCLUDED.name,
            details = EXCLUDED.details,
            iban = EXCLUDED.iban,
            account_type = EXCLUDED.account_type,
            product_name = EXCLUDED.product_name;";

        foreach (var account in accounts)
        {
            await connection.ExecuteAsync(sql, new
            {
                ConnectionId = externalConnectionId,
                Hash = account.IdentificationHash,
                AccountType = account.CashAccountType.ToString(),
                ProductName = account.Product,
                account.Name,
                account.Details,
                account.AccountId?.Iban,
                account.Currency
            });
        }
    }

    public async Task SyncExternalConnection(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string getExternalConnectionSql = @"
        SELECT *
        FROM external_connections
        WHERE id = @Id";
        var externalConnection = await connection.QueryFirstAsync<ExternalConnection>(getExternalConnectionSql, new
        {
            Id = externalConnectionId
        });

        if (externalConnection.ExpiresAt <= DateTimeOffset.Now)
        {
            await StartAuthorizationAsync(externalConnectionId, await _userService.GetCurrentUserId());
            externalConnection = await connection.QueryFirstAsync<ExternalConnection>(getExternalConnectionSql, new
            {
                Id = externalConnectionId
            });
        }

        var sessionDataResponse = await _client.Sessions[Guid.Parse(externalConnection.SessionId)].GetAsync();

        foreach (var accountId in sessionDataResponse.Accounts)
        {
            var balance = await _client.Accounts[accountId.Value].Balances.GetAsync();
        }
    }
}
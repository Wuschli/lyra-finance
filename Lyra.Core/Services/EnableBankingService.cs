using System.Security.Claims;
using Dapper;
using Lyra.Core.EnableBanking.Models;
using Lyra.Core.EnableBanking;

namespace Lyra.Core.Services;

public class EnableBankingService
{
    private readonly ApiClient _client;
    private readonly IDbConnectionFactory _connectionFactory;

    public EnableBankingService(ApiClient client, IDbConnectionFactory connectionFactory)
    {
        _client = client;
        _connectionFactory = connectionFactory;
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
        await PersistSession(externalConnectionId, sessionResponse);
    }

    private async Task PersistSession(Guid externalConnectionId, AuthorizeSessionResponse sessionResponse)
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
            sessionResponse.SessionId,
            sessionResponse.Access?.ValidUntil
        });
    }
}
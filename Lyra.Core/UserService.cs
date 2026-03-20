using Dapper;

namespace Lyra.Core;

public class UserService : IUserService
{
    private readonly IDbConnectionFactory _dbFactory;

    public UserService(IDbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
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
}
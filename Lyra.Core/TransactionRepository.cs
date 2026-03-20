using Dapper;

namespace Lyra.Core;

public class TransactionRepository
{
    private readonly IDbConnectionFactory _dbFactory;

    public TransactionRepository(IDbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(Guid userId)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        return await db.QueryAsync<Transaction>(
            "SELECT * FROM transactions WHERE user_id = @userId",
            new { userId }
        );
    }
}
using Dapper;
using Lyra.Core.Models;

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
            """
            SELECT t.*
            FROM lyra.transactions t
            INNER JOIN lyra.accounts a ON a.id = t.account_id
            WHERE a.user_id = @userId
            """,
            new { userId }
        );
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsByAccountAsync(Guid accountId)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        return await db.QueryAsync<Transaction>(
            """
            SELECT *
            FROM lyra.transactions
            WHERE account_id = @accountId
            ORDER BY transaction_date DESC
            """,
            new { accountId }
        );
    }

    public async Task SetCategoryAsync(Guid transactionId, string? category)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        await db.ExecuteAsync(
            """
            UPDATE lyra.transactions
            SET category = @category
            WHERE id = @transactionId
            """,
            new { transactionId, category }
        );
    }

    public async Task<IEnumerable<string>> GetDistinctCategoriesAsync(Guid userId)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        return await db.QueryAsync<string>(
            """
            SELECT DISTINCT t.category
            FROM lyra.transactions t
            INNER JOIN lyra.accounts a ON a.id = t.account_id
            WHERE a.user_id = @userId
              AND t.category IS NOT NULL
              AND t.category <> ''
            ORDER BY t.category ASC
            """,
            new { userId }
        );
    }
}
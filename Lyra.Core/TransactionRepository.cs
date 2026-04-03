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

    public async Task<IEnumerable<Transaction>> GetTransactionsByUserAsync(Guid userId, DateTimeOffset from, DateTimeOffset to)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        return await db.QueryAsync<Transaction>(
            """
            SELECT t.*
            FROM lyra.transactions t
            INNER JOIN lyra.accounts a ON a.id = t.account_id
            WHERE a.user_id = @userId
              AND t.transaction_date >= @from
              AND t.transaction_date < @to
            ORDER BY t.transaction_date DESC
            """,
            new { userId, from, to }
        );
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsByAccountAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        return await db.QueryAsync<Transaction>(
            """
            SELECT *
            FROM lyra.transactions
            WHERE account_id = @accountId
              AND transaction_date >= @from
              AND transaction_date < @to
            ORDER BY transaction_date DESC
            """,
            new { accountId, from, to }
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

    /// <summary>
    /// Links two transactions bidirectionally to represent an internal transfer between accounts.
    /// Pass <c>null</c> for both IDs on each transaction to remove an existing link.
    /// </summary>
    public async Task LinkTransactionsAsync(Guid transactionAId, Guid transactionBId)
    {
        using var db = await _dbFactory.CreateConnectionAsync();
        await db.ExecuteAsync(
            """
            UPDATE lyra.transactions
            SET linked_transaction_id = CASE
                WHEN id = @transactionAId THEN @transactionBId
                WHEN id = @transactionBId THEN @transactionAId
            END
            WHERE id IN (@transactionAId, @transactionBId)
            """,
            new { transactionAId, transactionBId }
        );
    }

    /// <summary>
    /// Removes the transfer link from both sides of a previously linked pair.
    /// </summary>
    public async Task UnlinkTransactionAsync(Guid transactionId)
    {
        using var db = await _dbFactory.CreateConnectionAsync();

        // Find the linked partner first, then clear both sides atomically.
        await db.ExecuteAsync(
            """
            UPDATE lyra.transactions
            SET linked_transaction_id = NULL
            WHERE id = @transactionId
               OR id = (
                   SELECT linked_transaction_id
                   FROM lyra.transactions
                   WHERE id = @transactionId
               )
            """,
            new { transactionId }
        );
    }

    /// <summary>
    /// Finds unlinked transactions belonging to the same user that have the opposite amount
    /// (i.e. <c>-amount</c>) and a transaction date within <paramref name="windowDays"/> days,
    /// on a different account than the source transaction.
    /// </summary>
    public async Task<IEnumerable<Transaction>> FindTransferCandidatesAsync(
        Guid userId,
        Guid sourceTransactionId,
        decimal amount,
        DateTimeOffset transactionDate,
        int windowDays = 5)
    {
        var from = transactionDate.AddDays(-windowDays);
        var to   = transactionDate.AddDays(windowDays);

        using var db = await _dbFactory.CreateConnectionAsync();
        return await db.QueryAsync<Transaction>(
            """
            SELECT t.*
            FROM lyra.transactions t
            INNER JOIN lyra.accounts a ON a.id = t.account_id
            WHERE a.user_id = @userId
              AND t.id <> @sourceTransactionId
              AND t.amount = @oppositeAmount
              AND t.transaction_date >= @from
              AND t.transaction_date <= @to
              AND t.linked_transaction_id IS NULL
              AND a.id <> (
                  SELECT account_id FROM lyra.transactions WHERE id = @sourceTransactionId
              )
            ORDER BY ABS(EXTRACT(EPOCH FROM (t.transaction_date - @transactionDate)))
            """,
            new
            {
                userId,
                sourceTransactionId,
                oppositeAmount = -amount,
                from,
                to,
                transactionDate
            }
        );
    }

    /// <summary>
    /// Batch variant of <see cref="FindTransferCandidatesAsync"/>. For each supplied source
    /// transaction returns whether at least one transfer candidate exists, using a single
    /// database round-trip via a LATERAL join.
    /// </summary>
    /// <returns>
    /// A <see cref="HashSet{T}"/> containing the IDs of every source transaction that has
    /// at least one matching candidate.
    /// </returns>
    public async Task<HashSet<Guid>> FindTransferCandidateSourceIdsAsync(
        Guid userId,
        IEnumerable<Transaction> sourceTransactions,
        int windowDays = 5)
    {
        var sources = sourceTransactions
            .Select(t => new { t.Id, t.Amount, t.TransactionDate, t.AccountId })
            .ToList();

        if (sources.Count == 0)
            return [];

        // Build a VALUES list so PostgreSQL receives all sources in one query.
        // Each row: (source_id, opposite_amount, date_from, date_to, account_id)
        var rows = sources.Select((s, i) => $"""
            (@id{i}::uuid, @oppositeAmount{i}::numeric, @from{i}::timestamptz, @to{i}::timestamptz, @accountId{i}::uuid)
            """);

        var valuesList = string.Join(",\n", rows);

        var sql = $"""
            SELECT DISTINCT src.source_id
            FROM (VALUES
                {valuesList}
            ) AS src(source_id, opposite_amount, date_from, date_to, account_id)
            WHERE EXISTS (
                SELECT 1
                FROM lyra.transactions t
                INNER JOIN lyra.accounts a ON a.id = t.account_id
                WHERE a.user_id = @userId
                  AND t.id <> src.source_id
                  AND t.amount = src.opposite_amount
                  AND t.transaction_date >= src.date_from
                  AND t.transaction_date <= src.date_to
                  AND t.linked_transaction_id IS NULL
                  AND t.account_id <> src.account_id
            )
            """;

        var parameters = new DynamicParameters();
        parameters.Add("userId", userId);

        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            parameters.Add($"id{i}", s.Id);
            parameters.Add($"oppositeAmount{i}", -s.Amount);
            parameters.Add($"from{i}", s.TransactionDate.AddDays(-windowDays));
            parameters.Add($"to{i}", s.TransactionDate.AddDays(windowDays));
            parameters.Add($"accountId{i}", s.AccountId);
        }

        using var db = await _dbFactory.CreateConnectionAsync();
        var ids = await db.QueryAsync<Guid>(sql, parameters);
        return ids.ToHashSet();
    }
}
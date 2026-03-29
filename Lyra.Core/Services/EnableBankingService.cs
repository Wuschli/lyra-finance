using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Lyra.Core.EnableBanking;
using Lyra.Core.EnableBanking.Models;
using Lyra.Core.Extensions;
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
            Access = new Access { ValidUntil = DateTimeOffset.Now.AddDays(90) },
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

    public async Task SetBalanceTypeAsync(Guid externalConnectionId, string? balanceType)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            UPDATE external_connections
            SET balance_type = @BalanceType
            WHERE id = @Id";

        await connection.ExecuteAsync(sql, new { Id = externalConnectionId, BalanceType = balanceType });
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

    public async Task<SyncStatus> SyncExternalConnection(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        var logBuilder = new StringBuilder();
        var status = SyncStatus.InProgress;

        try
        {
            const string getExternalConnectionSql = @"
                SELECT *
                FROM external_connections
                WHERE id = @Id";
            var externalConnection = await connection.QueryFirstAsync<ExternalConnection>(getExternalConnectionSql, new { Id = externalConnectionId });

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var sessionDataResponse = await _client.Sessions[Guid.Parse(externalConnection.SessionId)].GetAsync();
            logBuilder.AppendLine($"SessionDataResponse: {JsonSerializer.Serialize(sessionDataResponse, jsonOptions)}");

            for (int i = 0; i < sessionDataResponse.AccountsData.Count; i++)
            {
                var externalAccountData = sessionDataResponse.AccountsData[i];
                var externalAccountId = sessionDataResponse.Accounts[i];

                // Get internal account_id and iban by using the connection table
                const string getAccountIdAndIbanSql = @"
                    SELECT eca.account_id, eba.iban
                    FROM external_connection_account eca
                    JOIN enable_banking_accounts eba
                            ON eba.external_connection_id = eca.connection_id
                        AND eba.identification_hash = @IdentificationHash
                        WHERE eca.connection_id = @ConnectionId
                        AND eca.external_account_id = @ExternalAccountId";

                var result = await connection.QueryFirstOrDefaultAsync<(Guid? AccountId, string Iban)>(
                    getAccountIdAndIbanSql,
                    new
                    {
                        ConnectionId = externalConnectionId,
                        ExternalAccountId = externalAccountData.IdentificationHash,
                        externalAccountData.IdentificationHash
                    }
                );

                if (result.AccountId == null)
                {
                    logBuilder.AppendLine($"No internal account_id found for external_account_id {externalAccountData.IdentificationHash}");
                    continue;
                }

                var accountId = result.AccountId.Value;
                var iban = result.Iban ?? string.Empty;

                var balance = await _client.Accounts[externalAccountId.Value].Balances.GetAsync();
                logBuilder.AppendLine($"Account {accountId} Balance:\n{JsonSerializer.Serialize(balance, jsonOptions)}");

                if (externalConnection.BalanceType != null)
                {
                    await UpdateAccountBalance(accountId, balance, externalConnection.BalanceType, logBuilder);
                }

                var transactions = await _client.Accounts[externalAccountId.Value].Transactions.GetAsync();
                logBuilder.AppendLine($"Account {accountId} Transactions:\n{JsonSerializer.Serialize(transactions, jsonOptions)}");

                await UpsertExternalTransactions(accountId, iban, transactions?.Transactions ?? Enumerable.Empty<EnableBanking.Models.Transaction>(), logBuilder);
            }

            status = SyncStatus.Success;
        }
        catch (ErrorResponse ex) when (ex.Error is ErrorCode.EXPIRED_SESSION or ErrorCode.CLOSED_SESSION or ErrorCode.REVOKED_SESSION)
        {
            status = SyncStatus.SessionExpired;
            logBuilder.AppendLine($"Session expired (code: {ex.Error}): {ex.Message}");
        }
        catch (Exception ex)
        {
            status = SyncStatus.Failure;
            logBuilder.AppendLine($"Exception: {ex}");
        }
        finally
        {
            await LogSyncResult(connection, externalConnectionId, status, logBuilder.ToString());
        }

        return status;
    }

    private async Task UpdateAccountBalance(Guid accountId, HalBalances? halBalances, string balanceType, StringBuilder logBuilder)
    {
        var match = halBalances?.Balances?.FirstOrDefault(b => b.BalanceType?.ToString() == balanceType);
        if (match == null)
        {
            logBuilder.AppendLine($"Account {accountId}: No balance entry found for type '{balanceType}'. Available: {string.Join(", ", halBalances?.Balances?.Select(b => b.BalanceType?.ToString()) ?? [])}");
            return;
        }

        if (!decimal.TryParse(match.BalanceAmount?.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            logBuilder.AppendLine($"Account {accountId}: Could not parse balance amount '{match.BalanceAmount?.Amount}' for type '{balanceType}'.");
            return;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            UPDATE lyra.accounts
            SET current_balance = @Balance,
                current_balance_at = @BalanceAt
            WHERE id = @AccountId";

        await connection.ExecuteAsync(sql, new
        {
            AccountId = accountId,
            Balance = amount,
            BalanceAt = match.LastChangeDateTime ?? DateTimeOffset.Now
        });

        logBuilder.AppendLine($"Account {accountId}: Updated balance to {amount} {match.BalanceAmount?.Currency} (type: {balanceType}).");
    }

    private static string GenerateTransactionHash(EnableBanking.Models.Transaction transaction, Guid accountId)
    {
        // Combine immutable fields into a unique hash
        var hashInput = string.Concat(
            accountId,
            transaction.BookingDate?.ToString() ?? string.Empty,
            transaction.TransactionDate?.ToString() ?? string.Empty,
            transaction.CreditDebitIndicator?.ToString() ?? string.Empty,
            transaction.TransactionAmount?.Amount ?? string.Empty,
            transaction.TransactionAmount?.Currency ?? string.Empty,
            (transaction.Debtor?.Name ?? string.Empty).Trim(),
            (transaction.Creditor?.Name ?? string.Empty).Trim(),
            string.Join("|", transaction.RemittanceInformation ?? new List<string>())
        );

        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashedBytes);
    }

    private async Task UpsertExternalTransactions(Guid accountId,
        string iban,
        IEnumerable<EnableBanking.Models.Transaction> transactions,
        StringBuilder logBuilder)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        var transactionList = transactions.ToList();
        if (transactionList.Count == 0)
        {
            logBuilder.AppendLine($"Account {accountId}: No transactions to upsert.");
            return;
        }

        const string sql = @"
            INSERT INTO lyra.transactions
                (account_id, counterparty_name, counterparty_iban, description, amount, transaction_date, booking_date, value_date, external_identifier)
            VALUES
                (@AccountId, @CounterpartyName, @CounterpartyIban, @Description, @Amount, @TransactionDate, @BookingDate, @ValueDate, @ExternalIdentifier)
            ON CONFLICT (account_id, external_identifier)
            DO UPDATE SET
                counterparty_name = EXCLUDED.counterparty_name,
                counterparty_iban = EXCLUDED.counterparty_iban,
                description = EXCLUDED.description,
                amount = EXCLUDED.amount,
                transaction_date = EXCLUDED.transaction_date,
                booking_date = EXCLUDED.booking_date,
                value_date = EXCLUDED.value_date;";

        var parameters = transactionList.Select(transaction =>
        {
            var transactionDate = transaction.TransactionDate.ToDateTimeOrNull() ?? transaction.BookingDate.ToDateTimeOrNull() ?? DateTime.MinValue;

            var counterpartyName = transaction.Creditor?.Name ?? transaction.CreditorAgent?.Name ?? string.Empty;
            var counterpartyIban = transaction.CreditorAccount?.Iban ?? string.Empty;

            if (!decimal.TryParse(transaction.TransactionAmount?.Amount ?? string.Empty, out var amount))
            {
                //TODO ERROR
            }

            if (counterpartyIban == iban)
            {
                counterpartyName = transaction.Debtor?.Name ?? transaction.DebtorAgent?.Name ?? string.Empty;
                counterpartyIban = transaction.CreditorAccount?.Iban ?? string.Empty;
            }
            else
            {
                amount = -amount;
            }

            return new
            {
                AccountId = accountId,
                CounterpartyName = counterpartyName,
                CounterpartyIban = counterpartyIban,
                Description = string.Join("; ", transaction.RemittanceInformation ?? new List<string>()),
                Amount = amount,
                TransactionDate = transactionDate,
                BookingDate = transaction.BookingDate.ToDateTimeOrNull(),
                ValueDate = transaction.ValueDate.ToDateTimeOrNull(),
                ExternalIdentifier = GenerateTransactionHash(transaction, accountId)
            };
        }).ToList();

        await connection.ExecuteAsync(sql, parameters);
        logBuilder.AppendLine($"Account {accountId}: Upserted {transactionList.Count} transactions.");
    }

    private async Task LogSyncResult(
        IDbConnection connection,
        Guid connectionId,
        SyncStatus status,
        string message)
    {
        const string logSql = @"
            INSERT INTO lyra.sync_logs (connection_id, status, message, sync_end)
            VALUES (@ConnectionId, @Status, @Message, @SyncEnd);";

        await connection.ExecuteAsync(logSql, new
        {
            ConnectionId = connectionId,
            Status = status.ToString(),
            Message = message,
            SyncEnd = DateTimeOffset.Now
        });
    }

    public async Task<ExternalConnection?> GetExternalConnectionByAccountIdAsync(Guid accountId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT ec.*
            FROM lyra.external_connections ec
            JOIN lyra.external_connection_account eca ON eca.connection_id = ec.id
            WHERE eca.account_id = @AccountId";

        return await connection.QueryFirstOrDefaultAsync<ExternalConnection>(sql, new { AccountId = accountId });
    }
}
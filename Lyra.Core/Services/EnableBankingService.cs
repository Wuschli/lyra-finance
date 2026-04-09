using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Lyra.Core.EnableBanking;
using Lyra.Core.EnableBanking.Models;
using Lyra.Core.Extensions;
using Lyra.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Lyra.Core.Services;

public class EnableBankingService
{
    private readonly ApiClient _client;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly UserService _userService;
    private readonly AccountNotificationService _accountNotificationService;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public EnableBankingService(ApiClient client, IDbConnectionFactory connectionFactory, UserService userService, AccountNotificationService accountNotificationService, IConfiguration configuration)
    {
        _client = client;
        _connectionFactory = connectionFactory;
        _userService = userService;
        _accountNotificationService = accountNotificationService;
        _baseUrl = configuration["BaseUrl"]
                   ?? throw new InvalidOperationException("BaseUrl is not configured.");
    }


    /// <summary>
    /// Starts the user authorization flow (POST /sessions).
    /// Persists the ASPSP as provider_data JSON so the session can be re-authorized later.
    /// </summary>
    public async Task<AuthorizationResult> StartAuthorizationAsync(Guid externalConnectionId, ASPSP aspsp)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        var userId = await _userService.GetCurrentUserId();
        var providerDataJson = JsonSerializer.Serialize(aspsp, JsonOptions);

        const string sql = @"
            INSERT INTO external_connections 
                (id, user_id, provider_name, connection_name, provider_data, created_at)
            VALUES 
                (@Id, @UserId, 'enable_banking', @ConnectionName, @ProviderData::jsonb, @Now)
            ON CONFLICT (id)
            DO UPDATE SET
                provider_data = EXCLUDED.provider_data;";

        await connection.ExecuteAsync(sql, new
        {
            Id = externalConnectionId,
            UserId = userId,
            ConnectionName = aspsp.Name ?? string.Empty,
            ProviderData = providerDataJson,
            DateTimeOffset.Now
        });

        var authRequest = new StartAuthorizationRequest
        {
            Access = new Access { ValidUntil = DateTimeOffset.Now.AddDays(90) },
            Aspsp = aspsp,
            State = externalConnectionId.ToString(),
            RedirectUrl = $"{_baseUrl}/enable_banking/callback",
            PsuType = PSUType.Personal
        };

        var authResponse = await _client.Auth.PostAsync(authRequest);
        return authResponse?.Url != null
            ? AuthorizationResult.Success(authResponse.Url)
            : AuthorizationResult.Failed();
    }

    /// <summary>
    /// Re-initiates the authorization flow for an existing connection whose session has expired,
    /// using the ASPSP data that was stored when the connection was first created.
    /// </summary>
    public async Task<AuthorizationResult> ReauthorizeAsync(Guid externalConnectionId)
    {
        var userId = await _userService.GetCurrentUserId();
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT provider_data
            FROM external_connections
            WHERE id = @Id AND user_id = @UserId";

        var providerDataJson = await connection.QueryFirstOrDefaultAsync<string>(sql, new { Id = externalConnectionId, UserId = userId });

        if (string.IsNullOrEmpty(providerDataJson))
            throw new InvalidOperationException($"No provider data found for connection {externalConnectionId}. Cannot re-authorize.");

        var aspsp = JsonSerializer.Deserialize<ASPSP>(providerDataJson, JsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize stored ASPSP provider data.");

        return await StartAuthorizationAsync(externalConnectionId, aspsp);
    }

    /// <summary>
    /// Updates the user-visible display name of an external connection.
    /// </summary>
    public async Task UpdateConnectionNameAsync(Guid externalConnectionId, string connectionName)
    {
        var userId = await _userService.GetCurrentUserId();
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            UPDATE external_connections
            SET connection_name = @ConnectionName
            WHERE id = @Id AND user_id = @UserId";

        await connection.ExecuteAsync(sql, new
        {
            Id = externalConnectionId,
            UserId = userId,
            ConnectionName = connectionName
        });
    }

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

    /// <summary>
    /// Persists the list of balance types observed during a sync so the UI can offer
    /// only those types that the bank actually returns.
    /// </summary>
    public async Task SetAvailableBalanceTypesAsync(Guid externalConnectionId, IEnumerable<string> balanceTypes)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        var json = JsonSerializer.Serialize(balanceTypes.Distinct().OrderBy(x => x).ToList(), JsonOptions);

        const string sql = @"
            UPDATE external_connections
            SET available_balance_types = @Json::jsonb
            WHERE id = @Id";

        await connection.ExecuteAsync(sql, new { Id = externalConnectionId, Json = json });
    }

    /// <summary>
    /// Returns all external connections belonging to the current user.
    /// </summary>
    public async Task<IEnumerable<ExternalConnection>> GetConnectionsForCurrentUserAsync()
    {
        var userId = await _userService.GetCurrentUserId();
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT *,
                   available_balance_types AS available_balance_types_json
            FROM external_connections
            WHERE user_id = @UserId
            ORDER BY created_at ASC;";

        return await connection.QueryAsync<ExternalConnection>(sql, new { UserId = userId });
    }

    /// <summary>
    /// Returns all EnableBanking accounts for the given connection,
    /// together with the linked local account id (null if not linked).
    /// </summary>
    public async Task<IEnumerable<ExternalAccountWithLink>> GetExternalAccountsWithLinksAsync(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT
                eba.id,
                eba.external_connection_id,
                eba.identification_hash,
                eba.name,
                eba.details,
                eba.iban,
                eba.currency,
                eba.account_type AS cash_account_type,
                eba.product_name AS product,
                eca.account_id AS linked_account_id
            FROM enable_banking_accounts eba
            LEFT JOIN external_connection_account eca
                ON eca.connection_id = eba.external_connection_id
                AND eca.external_account_id = eba.identification_hash
            WHERE eba.external_connection_id = @ConnectionId;";

        return await connection.QueryAsync<ExternalAccountWithLink>(sql, new { ConnectionId = externalConnectionId });
    }

    /// <summary>
    /// Links an external account to an existing local account.
    /// </summary>
    public async Task LinkExternalAccountAsync(Guid externalConnectionId, string identificationHash, Guid localAccountId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            INSERT INTO external_connection_account (connection_id, external_account_id, account_id)
            VALUES (@ConnectionId, @ExternalAccountId, @AccountId)
            ON CONFLICT (connection_id, external_account_id)
            DO UPDATE SET account_id = EXCLUDED.account_id;";

        await connection.ExecuteAsync(sql, new
        {
            ConnectionId = externalConnectionId,
            ExternalAccountId = identificationHash,
            AccountId = localAccountId
        });
    }

    /// <summary>
    /// Removes the link between an external account and any local account.
    /// </summary>
    public async Task UnlinkExternalAccountAsync(Guid externalConnectionId, string identificationHash)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            DELETE FROM external_connection_account
            WHERE connection_id = @ConnectionId
            AND external_account_id = @ExternalAccountId;";

        await connection.ExecuteAsync(sql, new
        {
            ConnectionId = externalConnectionId,
            ExternalAccountId = identificationHash
        });
    }

    /// <summary>
    /// Deletes an external connection and all associated data for the current user.
    /// </summary>
    public async Task DeleteConnectionAsync(Guid externalConnectionId)
    {
        var userId = await _userService.GetCurrentUserId();
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            DELETE FROM external_connections
            WHERE id = @Id AND user_id = @UserId;";

        await connection.ExecuteAsync(sql, new { Id = externalConnectionId, UserId = userId });
    }

    /// <summary>
    /// Returns the most recent sync log entry for the given connection, or null if none exists.
    /// </summary>
    public async Task<SyncLog?> GetLastSyncLogAsync(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT *
            FROM lyra.sync_logs
            WHERE connection_id = @ConnectionId
            ORDER BY sync_end DESC NULLS LAST
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<SyncLog>(sql, new { ConnectionId = externalConnectionId });
    }

    /// <summary>
    /// Returns all sync log entries for the given connection, ordered by most recent first.
    /// </summary>
    public async Task<IEnumerable<SyncLog>> GetSyncLogsAsync(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT *
            FROM lyra.sync_logs
            WHERE connection_id = @ConnectionId
            ORDER BY sync_end DESC NULLS LAST;";

        return await connection.QueryAsync<SyncLog>(sql, new { ConnectionId = externalConnectionId });
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

    public async Task<SyncResult> SyncExternalConnection(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        var logBuilder = new StringBuilder();
        var status = SyncStatus.InProgress;

        var syncLogId = await CreateSyncLogAsync(connection, externalConnectionId);
        SyncRequestLoggingHandler.SetLogAction(message => logBuilder.AppendLine(message));

        try
        {
            const string getExternalConnectionSql = @"
                SELECT *,
                       available_balance_types AS available_balance_types_json
                FROM external_connections
                WHERE id = @Id";
            var externalConnection = await connection.QueryFirstAsync<ExternalConnection>(getExternalConnectionSql, new { Id = externalConnectionId });

            if (string.IsNullOrEmpty(externalConnection.SessionId))
            {
                AuthorizationResult result;
                try
                {
                    result = await ReauthorizeAsync(externalConnectionId);
                }
                catch (InvalidOperationException ex)
                {
                    logBuilder.AppendLine($"Re-authorization failed: {ex.Message}");
                    status = SyncStatus.Failure;
                    return SyncResult.Failure();
                }

                if (result.IsSuccess)
                {
                    logBuilder.AppendLine($"No active session — re-authorization required. URL: {result.RedirectUrl}");
                    status = SyncStatus.SessionExpired;
                    return SyncResult.NeedsReauthorization(result.RedirectUrl!);
                }

                throw new InvalidOperationException("Re-authorization response contained no redirect URL.");
            }

            var sessionDataResponse = await _client.Sessions[Guid.Parse(externalConnection.SessionId)].GetAsync();

            // Collect all balance types seen across all accounts in this sync pass.
            var discoveredBalanceTypes = new HashSet<string>();

            for (int i = 0; i < sessionDataResponse.AccountsData.Count; i++)
            {
                var externalAccountData = sessionDataResponse.AccountsData[i];
                var externalAccountId = sessionDataResponse.Accounts[i];

                try
                {
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
                    var iban = result.Iban;

                    var balance = await _client.Accounts[externalAccountId.Value].Balances.GetAsync();

                    // Collect all balance types returned by the API for this account.
                    foreach (var balanceType in balance?.Balances?.Select(b => b.BalanceType?.ToString()).OfType<string>() ?? [])
                    discoveredBalanceTypes.Add(balanceType);

                    // On the first sync of a new connection (BalanceType not yet set), auto-select
                    // the first available balance type returned by the API.
                    if (externalConnection.BalanceType == null)
                    {
                        var firstBalanceType = balance?.Balances?.FirstOrDefault()?.BalanceType?.ToString();
                        if (firstBalanceType != null)
                        {
                            externalConnection.BalanceType = firstBalanceType;
                            await SetBalanceTypeAsync(externalConnectionId, firstBalanceType);
                            logBuilder.AppendLine($"Auto-selected balance type '{firstBalanceType}' for new connection {externalConnectionId}.");
                        }
                    }

                    if (externalConnection.BalanceType != null)
                    {
                        await UpdateAccountBalance(accountId, balance, externalConnection.BalanceType, logBuilder);
                    }


                    var allTransactions = new List<EnableBanking.Models.Transaction>();
                    string? continuationKey = null;

                    do
                    {
                        var page = await _client.Accounts[externalAccountId.Value].Transactions.GetAsync(q =>
                        {
                            if (continuationKey != null)
                                q.QueryParameters.ContinuationKey = continuationKey;
                        });

                        if (page?.Transactions != null)
                            allTransactions.AddRange(page.Transactions);

                        continuationKey = page?.ContinuationKey;
                        logBuilder.AppendLine($"Account {accountId}: Fetched page with {page?.Transactions?.Count ?? 0} transactions, continuation_key: {continuationKey ?? "none"}.");
                    } while (!string.IsNullOrEmpty(continuationKey));

                    await UpsertExternalTransactions(accountId, iban, allTransactions, logBuilder);
                }
                catch (ErrorResponse ex) when (ex.Error is ErrorCode.EXPIRED_SESSION or ErrorCode.CLOSED_SESSION or ErrorCode.REVOKED_SESSION or ErrorCode.ASPSP_ERROR)
                {
                    // Session errors are fatal — re-throw so the outer catch can mark the connection as expired.
                    throw;
                }
                catch (ErrorResponse ex)
                {
                    status = SyncStatus.Failure;
                    logBuilder.AppendLine($"ASPSP error for account {externalAccountData.IdentificationHash} (code: {ex.Code}, error: {ex.Error}): {ex.Message}");
                }
            }

            // Persist the union of all balance types discovered across all accounts.
            if (discoveredBalanceTypes.Count > 0)
                await SetAvailableBalanceTypesAsync(externalConnectionId, discoveredBalanceTypes);

            if (status == SyncStatus.InProgress)
                status = SyncStatus.Success;
        }
        catch (ErrorResponse ex) when (ex.Error is ErrorCode.EXPIRED_SESSION or ErrorCode.CLOSED_SESSION or ErrorCode.REVOKED_SESSION or ErrorCode.ASPSP_ERROR)
        {
            status = SyncStatus.SessionExpired;
            logBuilder.AppendLine($"Session expired (code: {ex.Error}): {ex.Message}");

            var reauthorizeResult = await ReauthorizeAsync(externalConnectionId);
            if (reauthorizeResult.IsSuccess)
                logBuilder.AppendLine($"Re-authorization initiated. URL: {reauthorizeResult.RedirectUrl}");

            await FinalizeSyncLogAsync(connection, syncLogId, status, logBuilder.ToString());
            await _accountNotificationService.NotifyAccountsChangedAsync();

            return reauthorizeResult.IsSuccess
                ? SyncResult.NeedsReauthorization(reauthorizeResult.RedirectUrl!)
                : SyncResult.Failure();
        }
        catch (Exception ex)
        {
            status = SyncStatus.Failure;
            logBuilder.AppendLine($"Exception: {ex}");
        }
        finally
        {
            await FinalizeSyncLogAsync(connection, syncLogId, status, logBuilder.ToString());
        }

        await _accountNotificationService.NotifyAccountsChangedAsync();

        return status == SyncStatus.Success ? SyncResult.Success() : SyncResult.Failure();
    }

    private async Task<Guid> CreateSyncLogAsync(IDbConnection connection, Guid connectionId)
    {
        const string sql = @"
            INSERT INTO sync_logs (connection_id, status, sync_start)
            VALUES (@ConnectionId, @Status, @SyncStart)
            RETURNING id;";

        return await connection.ExecuteScalarAsync<Guid>(sql, new
        {
            ConnectionId = connectionId,
            Status = SyncStatus.InProgress.ToString(),
            SyncStart = DateTimeOffset.Now
        });
    }

    private async Task FinalizeSyncLogAsync(
        IDbConnection connection,
        Guid syncLogId,
        SyncStatus status,
        string message)
    {
        const string sql = @"
            UPDATE sync_logs
            SET status = @Status,
                message = @Message,
                sync_end = @SyncEnd
            WHERE id = @Id;";

        await connection.ExecuteAsync(sql, new
        {
            Id = syncLogId,
            Status = status.ToString(),
            Message = message,
            SyncEnd = DateTimeOffset.Now
        });
    }

    private async Task UpdateAccountBalance(Guid accountId, HalBalances? halBalances, string balanceType, StringBuilder logBuilder)
    {
        var match = halBalances?.Balances?.FirstOrDefault(b => b.BalanceType?.ToString() == balanceType);
        if (match == null)
        {
            logBuilder.AppendLine($"Account {accountId}: No balance entry found for type '{balanceType}'. Available: {string.Join(", ", halBalances?.Balances?.Select(b => b.BalanceType?.ToString()) ??[])}");
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

    /// <summary>
    /// Returns all transactions for all accounts linked to the given connection,
    /// including their external_identifier for rehashing.
    /// </summary>
    public async Task<IEnumerable<TransactionWithExternalIdentifier>> GetTransactionsForRehashAsync(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT t.id, t.account_id, t.counterparty_name, t.counterparty_iban, t.description,
                   t.amount, t.currency, t.transaction_date, t.booking_date, t.value_date, t.category,
                   t.external_identifier
            FROM lyra.transactions t
            JOIN external_connection_account eca ON eca.account_id = t.account_id
            WHERE eca.connection_id = @ConnectionId
            ORDER BY t.transaction_date DESC;";

        return await connection.QueryAsync<TransactionWithExternalIdentifier>(sql, new { ConnectionId = externalConnectionId });
    }

    private static string GenerateTransactionHash(
        EnableBanking.Models.Transaction transaction,
        Guid accountId,
        string counterpartyName,
        string counterpartyIban)
    {
        if (!string.IsNullOrWhiteSpace(transaction.EntryReference))
            return TransactionWithExternalIdentifier.FormatEntryReference(accountId, transaction.EntryReference);

        return TransactionWithExternalIdentifier.ComputeHash(
            accountId,
            creditDebitIndicator: transaction.CreditDebitIndicator?.ToString() ?? string.Empty,
            amount: transaction.TransactionAmount?.Amount ?? string.Empty,
            currency: transaction.TransactionAmount?.Currency ?? string.Empty,
            counterpartyName: counterpartyName,
            counterpartyIban: counterpartyIban,
            remittanceInformation: string.Join("|", transaction.RemittanceInformation ?? new List<string>())
        );
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
                (account_id, counterparty_name, counterparty_iban, description, amount, currency, transaction_date, booking_date, value_date, external_identifier, is_pending)
            VALUES
                (@AccountId, @CounterpartyName, @CounterpartyIban, @Description, @Amount, @Currency, @TransactionDate, @BookingDate, @ValueDate, @ExternalIdentifier, @IsPending)
            ON CONFLICT (account_id, external_identifier)
            DO UPDATE SET
                counterparty_name = EXCLUDED.counterparty_name,
                counterparty_iban = EXCLUDED.counterparty_iban,
                description = EXCLUDED.description,
                amount = EXCLUDED.amount,
                currency = EXCLUDED.currency,
                transaction_date = EXCLUDED.transaction_date,
                booking_date = EXCLUDED.booking_date,
                value_date = EXCLUDED.value_date,
                is_pending = EXCLUDED.is_pending;";

        var parameters = transactionList.Select(transaction =>
        {
            var isPending = transaction.Status != TransactionStatus.BOOK;
            var transactionDate = transaction.TransactionDate.ToDateTimeOrNull()
                                  ?? transaction.BookingDate.ToDateTimeOrNull()
                                  ?? (isPending ? DateTime.Now : DateTime.MinValue);
            var currency = transaction.TransactionAmount?.Currency ?? string.Empty;

            var counterpartyName = transaction.Creditor?.Name ?? transaction.CreditorAgent?.Name ?? string.Empty;
            var counterpartyIban = transaction.CreditorAccount?.Iban ?? string.Empty;

            if (!decimal.TryParse(transaction.TransactionAmount?.Amount ?? string.Empty, out var amount))
            {
                //TODO ERROR
            }

            if (counterpartyIban == iban)
            {
                counterpartyName = transaction.Debtor?.Name ?? transaction.DebtorAgent?.Name ?? string.Empty;
                counterpartyIban = transaction.DebtorAccount?.Iban ?? string.Empty;
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
                Currency = currency,
                TransactionDate = transactionDate,
                BookingDate = transaction.BookingDate.ToDateTimeOrNull(),
                ValueDate = transaction.ValueDate.ToDateTimeOrNull(),
                ExternalIdentifier = GenerateTransactionHash(transaction, accountId, counterpartyName, counterpartyIban),
                IsPending = isPending
            };
        }).ToList();

        await connection.ExecuteAsync(sql, parameters);
        logBuilder.AppendLine($"Account {accountId}: Upserted {transactionList.Count} transactions.");
    }

    /// <summary>
    /// Recomputes the external_identifier for all transactions linked to the given connection
    /// and updates rows whose stored hash differs from the current algorithm.
    /// Rows that were identified via EntryReference are skipped — their identifier is already stable.
    /// If two rows would get the same new hash (duplicate), the older one is deleted.
    /// Returns the number of updated/deleted rows.
    /// </summary>
    public async Task<int> RehashTransactionsForConnectionAsync(Guid externalConnectionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string fetchSql = @"
            SELECT t.id, t.account_id, t.counterparty_name, t.counterparty_iban,
                   t.amount, t.currency, t.description, t.external_identifier,
                   t.transaction_date
            FROM lyra.transactions t
            JOIN external_connection_account eca ON eca.account_id = t.account_id
            WHERE eca.connection_id = @ConnectionId;";

        var rows = (await connection.QueryAsync<(Guid Id, Guid AccountId, string CounterpartyName, string CounterpartyIban, decimal Amount, string Currency, string? Description, string? ExternalIdentifier, DateTimeOffset TransactionDate)>(
            fetchSql, new { ConnectionId = externalConnectionId })).ToList();

        // Rows identified by EntryReference are already stable — skip them entirely.
        var hashRows = rows
            .Where(r => !TransactionWithExternalIdentifier.IsEntryReferenceBased(r.ExternalIdentifier))
            .ToList();

        // Compute new hash for every remaining row, then group to find collisions.
        var withNewHash = hashRows.Select(row => (
            row.Id,
            row.AccountId,
            row.TransactionDate,
            OldHash: row.ExternalIdentifier,
            NewHash: TransactionWithExternalIdentifier.ComputeHash(
                row.AccountId,
                creditDebitIndicator: row.Amount < 0 ? "DBIT" : "CRDT",
                amount: Math.Abs(row.Amount).ToString(System.Globalization.CultureInfo.InvariantCulture),
                currency: row.Currency,
                counterpartyName: row.CounterpartyName,
                counterpartyIban: row.CounterpartyIban,
                remittanceInformation: row.Description ?? string.Empty
            )
        )).ToList();

        // Within each (account, new hash) group keep the newest row, delete the rest.
        var toDelete = withNewHash
            .GroupBy(r => (r.AccountId, r.NewHash))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderByDescending(r => r.TransactionDate).Skip(1))
            .Select(r => r.Id)
            .ToHashSet();

        const string deleteSql = @"DELETE FROM lyra.transactions WHERE id = @Id;";
        foreach (var id in toDelete)
            await connection.ExecuteAsync(deleteSql, new { Id = id });

        // Now update hashes for remaining rows that actually changed.
        const string updateSql = @"
            UPDATE lyra.transactions
            SET external_identifier = @NewHash
            WHERE id = @Id;";

        var updated = 0;
        foreach (var row in withNewHash.Where(r => !toDelete.Contains(r.Id)))
        {
            if (string.Equals(row.OldHash, row.NewHash, StringComparison.OrdinalIgnoreCase))
                continue;

            await connection.ExecuteAsync(updateSql, new { Id = row.Id, NewHash = row.NewHash });
            updated++;
        }

        return updated + toDelete.Count;
    }

    public async Task<ExternalConnection?> GetExternalConnectionByAccountIdAsync(Guid accountId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT ec.*,
                   ec.available_balance_types AS available_balance_types_json
            FROM external_connections ec
            JOIN external_connection_account eca ON eca.connection_id = ec.id
            WHERE eca.account_id = @AccountId";

        return await connection.QueryFirstOrDefaultAsync<ExternalConnection>(sql, new { AccountId = accountId });
    }

    /// <summary>
    /// Returns the most recent sync log for the connection linked to the given local account,
    /// or null if the account has no external connection or no sync has run yet.
    /// </summary>
    public async Task<SyncLog?> GetLastSyncLogByAccountIdAsync(Guid accountId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            SELECT sl.*
            FROM lyra.sync_logs sl
            JOIN external_connections ec ON ec.id = sl.connection_id
            JOIN external_connection_account eca ON eca.connection_id = ec.id
            WHERE eca.account_id = @AccountId
            ORDER BY sl.sync_end DESC NULLS LAST
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<SyncLog>(sql, new { AccountId = accountId });
    }

    /// <summary>
    /// Deletes the EnableBanking session for the given connection (revokes bank consent)
    /// and clears the stored session data locally so the user is prompted to re-authorize.
    /// </summary>
    public async Task DeleteSessionAsync(Guid externalConnectionId)
    {
        var userId = await _userService.GetCurrentUserId();
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string getSessionSql = @"
            SELECT session_id
            FROM external_connections
            WHERE id = @Id AND user_id = @UserId";

        var sessionId = await connection.QueryFirstOrDefaultAsync<string?>(getSessionSql, new { Id = externalConnectionId, UserId = userId });

        if (!string.IsNullOrEmpty(sessionId) && Guid.TryParse(sessionId, out var sessionGuid))
        {
            try
            {
                await _client.Sessions[sessionGuid].DeleteAsync();
            }
            catch (ErrorResponse)
            {
                // Session may already be gone on the EnableBanking side — ignore and clear locally.
            }
        }

        const string clearSql = @"
            UPDATE external_connections
            SET session_id = NULL,
                expires_at = NULL
            WHERE id = @Id AND user_id = @UserId";

        await connection.ExecuteAsync(clearSql, new { Id = externalConnectionId, UserId = userId });
    }

    /// <summary>
    /// Deletes a transaction by its ID.
    /// </summary>
    public async Task DeleteTransactionAsync(Guid transactionId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        const string sql = @"
            DELETE FROM lyra.transactions
            WHERE id = @Id;";

        await connection.ExecuteAsync(sql, new { Id = transactionId });
    }
}
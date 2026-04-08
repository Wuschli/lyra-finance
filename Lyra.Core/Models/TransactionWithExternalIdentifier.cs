using System.Security.Cryptography;
using System.Text;

namespace Lyra.Core.Models;

public class TransactionWithExternalIdentifier
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string CounterpartyIban { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset TransactionDate { get; set; }
    public DateTimeOffset? BookingDate { get; set; }
    public DateTimeOffset? ValueDate { get; set; }
    public string? Category { get; set; }
    public string? ExternalIdentifier { get; set; }

    /// <summary>
    /// Returns a stable, non-hash identifier for transactions that carry an EntryReference.
    /// The account ID is included because EntryReferences are only unique per account.
    /// </summary>
    public static string FormatEntryReference(Guid accountId, string entryReference)
        => $"ref:{accountId}:{entryReference}";

    /// <summary>
    /// Returns true when the stored external_identifier was produced by
    /// <see cref="FormatEntryReference"/> rather than <see cref="ComputeHash"/>.
    /// </summary>
    public static bool IsEntryReferenceBased(string? externalIdentifier)
        => externalIdentifier?.StartsWith("ref:", StringComparison.Ordinal) == true;

    /// <summary>
    /// Computes the canonical transaction hash from the resolved (post-direction-logic) fields.
    /// This is the single source of truth used by both the sync upsert and the duplicate scanner.
    /// Dates are intentionally excluded because BookingDate / TransactionDate can shift between syncs.
    /// </summary>
    public static string ComputeHash(
        Guid accountId,
        string creditDebitIndicator,
        string amount,
        string currency,
        string counterpartyName,
        string counterpartyIban,
        string remittanceInformation)
    {
        var hashInput = string.Concat(
            accountId,
            creditDebitIndicator,
            amount,
            currency,
            counterpartyName.Trim(),
            counterpartyIban.Trim(),
            remittanceInformation
        );

        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashedBytes);
    }
}
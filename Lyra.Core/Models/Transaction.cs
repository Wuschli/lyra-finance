namespace Lyra.Core.Models;

public class Transaction
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
}
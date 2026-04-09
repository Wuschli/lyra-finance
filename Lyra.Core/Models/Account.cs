namespace Lyra.Core.Models;

public class Account
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Optionally updated on sync based on the configured balance type
    public decimal? CurrentBalance { get; set; }
    public DateTimeOffset? CurrentBalanceAt { get; set; }
}
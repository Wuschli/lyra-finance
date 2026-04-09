namespace Lyra.Core.Models;

public class ExternalConnectionAccount
{
    public Guid Id { get; set; }

    // Reference to the parent connection (ExternalConnection.Id)
    public Guid ConnectionId { get; set; }

    // Reference to the internal account (Account.Id)
    public Guid AccountId { get; set; }

    // The unique ID for this specific account within the provider's system
    public string ExternalAccountId { get; set; } = string.Empty;
}
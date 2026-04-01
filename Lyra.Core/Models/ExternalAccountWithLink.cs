namespace Lyra.Core.Models;

public class ExternalAccountWithLink
{
    public Guid Id { get; set; }
    public Guid ExternalConnectionId { get; set; }
    public string IdentificationHash { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Details { get; set; }
    public string? Iban { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CashAccountType { get; set; }
    public string? Product { get; set; }

    // Null when not yet linked to a local account
    public Guid? LinkedAccountId { get; set; }
}
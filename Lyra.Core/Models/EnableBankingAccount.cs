using System.ComponentModel.DataAnnotations;

namespace Lyra.Core.Models;

public class EnableBankingAccount
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid ExternalConnectionId { get; set; }

    [Required]
    public string IdentificationHash { get; set; } = string.Empty;

    // Account Details
    public string? Name { get; set; }

    public string? Details { get; set; }

    public string? Iban { get; set; }

    [Required]
    public string Currency { get; set; } = string.Empty;

    public string? CashAccountType { get; set; }

    public string? Product { get; set; }
}
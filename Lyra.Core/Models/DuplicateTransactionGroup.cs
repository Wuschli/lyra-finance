namespace Lyra.Core.Models;

public class DuplicateTransactionGroup
{
    public string GroupKey { get; set; } = string.Empty;
    public List<TransactionWithExternalIdentifier> Duplicates { get; set; } = new();
}
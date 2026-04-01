namespace Lyra.Core.Models;

public class ExternalConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // The provider identifier, e.g., "enable_banking" or "salt_edge"
    public string ProviderName { get; set; } = string.Empty;

    // Human-readable display name, editable by the user
    public string ConnectionName { get; set; } = string.Empty;

    // The session or consent ID returned by the provider
    public string SessionId { get; set; } = string.Empty;

    // Timestamp when the current access token or consent expires
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Which balance type to use when syncing (e.g. CLBD, ITAV). Null = skip balance sync.
    public string? BalanceType { get; set; }

    // Provider-specific data stored as JSON (e.g. ASPSP for Enable Banking),
    // used to re-initiate authorization when the session expires.
    public string? ProviderData { get; set; }
}
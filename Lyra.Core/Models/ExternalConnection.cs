namespace Lyra.Core.Models;

public class ExternalConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // The provider identifier, e.g., "enable_banking" or "salt_edge"
    public string ProviderName { get; set; } = string.Empty;

    // The session or consent ID returned by the provider
    public string ExternalSessionId { get; set; } = string.Empty;

    // OAuth2 or API specific tokens
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }

    // Timestamp when the current access token or consent expires
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Track the last time a successful synchronization occurred for this connection
    public DateTimeOffset? LastSyncAt { get; set; }
}
namespace Lyra.Core.Models;

public class ExternalConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // The provider identifier, e.g., "enable_banking" or "salt_edge"
    public string ProviderName { get; set; } = string.Empty;

    // The session or consent ID returned by the provider
    public string SessionId { get; set; } = string.Empty;

    // Timestamp when the current access token or consent expires
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
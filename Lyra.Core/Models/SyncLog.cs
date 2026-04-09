namespace Lyra.Core.Models;

public class SyncLog
{
    public Guid Id { get; set; }

    // Reference to the connection that was synchronized
    public Guid ConnectionId { get; set; }

    public DateTimeOffset SyncStart { get; set; }
    public DateTimeOffset? SyncEnd { get; set; }

    // Status can be 'running', 'success', 'error', or 'partial'
    public string Status { get; set; } = "running";

    // Detailed error messages or summary (e.g., "Fetched 24 new transactions")
    public string? Message { get; set; }
}
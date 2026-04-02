namespace Lyra.Core.Models;

public class SyncResult
{
    public SyncStatus Status { get; init; }

    /// <summary>
    /// Set when the connection requires re-authorization.
    /// The caller is responsible for redirecting the user to this URL.
    /// </summary>
    public string? ReauthorizationUrl { get; init; }

    public bool RequiresReauthorization => ReauthorizationUrl != null;

    public static SyncResult Success() => new() { Status = SyncStatus.Success };
    public static SyncResult Failure() => new() { Status = SyncStatus.Failure };
    public static SyncResult SessionExpired() => new() { Status = SyncStatus.SessionExpired };
    public static SyncResult NeedsReauthorization(string url) => new() { Status = SyncStatus.SessionExpired, ReauthorizationUrl = url };
}
namespace Lyra.Core.Models;

public class User
{
    public Guid Id { get; set; }
    public string ZitadelId { get; set; } = string.Empty;
    public DateTimeOffset LastLogin { get; set; }
}

// Request to start a new authorization session
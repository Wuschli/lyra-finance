namespace Lyra.Core;

public interface IUserService
{
    Task<Guid> EnsureUserExists(string zitadelId);
}
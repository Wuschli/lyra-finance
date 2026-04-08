namespace Lyra.Core.Models;

public class AuthorizationResult
{
    /// <summary>
    /// The URL the user must be redirected to in order to authorize with the bank.
    /// </summary>
    public string? RedirectUrl { get; init; }

    public bool IsSuccess => RedirectUrl != null;

    public static AuthorizationResult Success(string redirectUrl) => new() { RedirectUrl = redirectUrl };
    public static AuthorizationResult Failed() => new();
}
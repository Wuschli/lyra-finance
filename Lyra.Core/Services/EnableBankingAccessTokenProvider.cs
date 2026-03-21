using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Kiota.Abstractions.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace Lyra.Core.Services;

public class EnableBankingAccessTokenProvider : IAccessTokenProvider, IDisposable
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;

    private bool _disposed;
    private string? _cachedToken;


    public AllowedHostsValidator AllowedHostsValidator { get; }

    public EnableBankingAccessTokenProvider(IConfiguration config)
    {
        var appId = config["EnableBanking:AppId"] ?? throw new ArgumentNullException("AppId missing");
        // Load the private key from your settings (PEM format)
        var privateKeyPem = config["EnableBanking:PrivateKey"] ?? throw new ArgumentNullException("PrivateKey missing");

        _rsa = RSA.Create();
        _rsa.ImportFromPem(privateKeyPem);

        _securityKey = new RsaSecurityKey(_rsa)
        {
            KeyId = appId
        };
    }

    public Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = new CancellationToken())
    {
        if (_cachedToken != null)
        {
            return Task.FromResult(_cachedToken);
        }

        _cachedToken = GenerateAuthToken();
        return Task.FromResult(_cachedToken);
    }


    /// <summary>
    /// Generates a JWT signed with the RS256 private key for API authentication.
    /// </summary>
    private string GenerateAuthToken()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var credentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);

        var header = new JwtHeader(credentials);
        var payload = new JwtPayload
        {
            { "iss", "enablebanking.com" },
            { "aud", "api.enablebanking.com" },
            { "iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            { "exp", DateTimeOffset.UtcNow.AddMinutes(90).ToUnixTimeSeconds() }
        };

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _rsa.Dispose();
        _disposed = true;
    }
}
using Lyra.Core.EnableBanking.Models;
using Lyra.Core.EnableBanking;

namespace Lyra.Core.Services;

public class EnableBankingService
{
    private readonly ApiClient _client;

    public EnableBankingService(ApiClient client)
    {
        _client = client;
    }


    /// <summary>
    /// Starts the user authorization flow (POST /sessions).
    /// </summary>
    public async Task<StartAuthorizationResponse?> CreateSessionAsync()
    {
        var authRequest = new StartAuthorizationRequest
        {
            Access = new Access { ValidUntil = DateTimeOffset.UtcNow.AddDays(90) },
            Aspsp = new ASPSP { Name = "Sparkasse Hildesheim Goslar Peine", Country = "DE" }, // TODO as parameter
            State = Guid.NewGuid().ToString(),
            RedirectUrl = "https://localhost:7001/enable_banking/callback", // TODO base url from config
            PsuType = PSUType.Personal
        };


        var authResponse = await _client.Auth.PostAsync(authRequest);

        //var sessionRequest = new AuthorizeSessionRequest
        //{
        //    Code = authResponse.AuthorizationId.ToString()
        //};

        //var sessionResponse = await _client.Sessions.PostAsync(sessionRequest);

        return authResponse;
    }
}
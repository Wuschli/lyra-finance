using Lyra.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lyra.Core;

[ApiController]
[Route("enable_banking")]
public class EnableBankingController : ControllerBase
{
    private readonly EnableBankingService _bankingService;
    private readonly ILogger<EnableBankingController> _logger;

    public EnableBankingController(EnableBankingService bankingService, ILogger<EnableBankingController> logger, AuthenticationStateProvider authStateProvider)
    {
        _bankingService = bankingService;
        _logger = logger;
    }

    [HttpGet("callback")]
    [Authorize]
    public async Task<IActionResult> Callback([FromQuery] Guid? state, [FromQuery] string? code, [FromQuery] string? error)
    {

        //// 1. Check for errors from the bank provider
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Banking authorization failed: {Error}", error);
            return Redirect($"/?error={error}");
        }

        if (state == null || string.IsNullOrEmpty(code))
        {
            return BadRequest("Missing required callback parameters.");
        }

        try
        {
            // 2. Finalize the connection 
            // This usually involves exchanging the code for a permanent access token
            // and saving it into your 'external_connections' table.
            await _bankingService.FinalizeConnectionAsync(state.Value, code);

            // 3. Redirect the user back to the UI (e.g., your Blazor account settings)
            return Redirect("/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during banking finalization for state: {State}", state);
            return Redirect("/?error=internal_error");
        }
    }
}
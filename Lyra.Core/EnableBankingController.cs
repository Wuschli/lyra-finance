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
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Banking authorization failed: {Error}", error);
            return Redirect($"/settings/connections?error={error}");
        }

        if (state == null || string.IsNullOrEmpty(code))
        {
            return BadRequest("Missing required callback parameters.");
        }

        try
        {
            await _bankingService.FinalizeConnectionAsync(state.Value, code);
            return Redirect("/settings/connections");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during banking finalization for state: {State}", state);
            return Redirect("/settings/connections?error=internal_error");
        }
    }
}
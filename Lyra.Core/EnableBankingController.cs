using Lyra.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lyra.Core;

[ApiController]
[Route("enable_banking")]
public class EnableBankingController : ControllerBase
{
    private readonly EnableBankingService _bankingService;
    private readonly ILogger<EnableBankingController> _logger;

    public EnableBankingController(EnableBankingService bankingService, ILogger<EnableBankingController> logger)
    {
        _bankingService = bankingService;
        _logger = logger;
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code, [FromQuery] string? error)
    {
        return Empty;
        //// 1. Check for errors from the bank provider
        //if (!string.IsNullOrEmpty(error))
        //{
        //    _logger.LogWarning("Banking authorization failed: {Error}", error);
        //    return Redirect($"/settings/accounts?error={error}");
        //}

        //if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(code))
        //{
        //    return BadRequest("Missing required callback parameters.");
        //}

        //try
        //{
        //    // 2. Finalize the connection 
        //    // This usually involves exchanging the code for a permanent access token
        //    // and saving it into your 'external_connections' table.
        //    await _bankingService.FinalizeConnectionAsync(state, code);

        //    // 3. Redirect the user back to the UI (e.g., your Blazor account settings)
        //    return Redirect("/settings/accounts?sync=success");
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, "Error during banking finalization for state: {State}", state);
        //    return Redirect("/settings/accounts?error=internal_error");
        //}
    }
}
using Azure.Core;
using Microsoft.AspNetCore.Mvc;

namespace SvcTestApp.Controllers;

/// <summary>
/// Controller to provide authentication status information
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthStatusController : ControllerBase
{
    private readonly ILogger<AuthStatusController> _logger;
    private readonly AuthenticationStatus _authStatus;
    private readonly KeyVaultHealthStatus _keyVaultStatus;

    public AuthStatusController(
        ILogger<AuthStatusController> logger,
        AuthenticationStatus authStatus,
        KeyVaultHealthStatus keyVaultStatus)
    {
        _logger = logger;
        _authStatus = authStatus;
        _keyVaultStatus = keyVaultStatus;
    }

    /// <summary>
    /// Get current authentication status
    /// </summary>
    /// <returns>Authentication method and Key Vault status</returns>
    [HttpGet]
    public IActionResult GetAuthStatus()
    {
        try
        {
            var status = new
            {
                AuthenticationMethod = _authStatus.Method,
                ConfiguredFromKeyVault = _authStatus.IsConfiguredFromKeyVault,
                KeyVaultAvailable = _keyVaultStatus.IsAvailable,
                Status = _authStatus.IsConfiguredFromKeyVault ? "Service Principal Active" : "Fallback Authentication",
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Authentication status requested: {Method}, KeyVault: {Available}", 
                _authStatus.Method, _keyVaultStatus.IsAvailable);

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving authentication status");
            return StatusCode(500, new { error = "Failed to retrieve authentication status" });
        }
    }
}
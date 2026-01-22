using System.Data;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using MiKvSqlRazor.Models;

namespace MiKvSqlRazor.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IConfiguration _config;
    private readonly SecretClient _secretClient;
    private readonly TokenCredential _credential;

    public string SampleSecretName { get; set; } = string.Empty;
    public AuthMethod SelectedAuthMethod { get; set; }
    public string CurrentAuthMethodDescription { get; set; } = string.Empty;
    
    // Token Acquisition
    public bool TokenAcquisitionOk { get; set; }
    public string TokenAcquisitionValue { get; set; } = string.Empty;
    public string TokenAcquisitionError { get; set; } = string.Empty;
    public string TokenAcquisitionMessage { get; set; } = string.Empty;
    
    // Key Vault
    public bool KeyVaultOk { get; set; }
    public string KeyVaultValue { get; set; } = string.Empty;
    public string KeyVaultError { get; set; } = string.Empty;
    public string KeyVaultMessage { get; set; } = string.Empty;
    
    // SQL
    public bool SqlOk { get; set; }
    public string SqlProbeResult { get; set; } = string.Empty;
    public string SqlError { get; set; } = string.Empty;
    public string SqlMessage { get; set; } = string.Empty;
    
    // Overall Status
    public bool OverallHealthy { get; set; }
    public string OverallStatusMessage { get; set; } = string.Empty;

    public IndexModel(ILogger<IndexModel> logger, IConfiguration config, SecretClient secretClient, TokenCredential credential)
    {
        _logger = logger;
        _config = config;
        _secretClient = secretClient;
        _credential = credential;
    }

    public async Task OnGetAsync(string? selectedAuthMethod, CancellationToken cancellationToken)
    {
        // Parse the selected authentication method
        if (!string.IsNullOrEmpty(selectedAuthMethod) && Enum.TryParse<AuthMethod>(selectedAuthMethod, out var method))
        {
            SelectedAuthMethod = method;
        }
        else
        {
            SelectedAuthMethod = AuthMethod.ManagedIdentity;
        }

        CurrentAuthMethodDescription = SelectedAuthMethod switch
        {
            AuthMethod.ManagedIdentity => "Managed Identity",
            AuthMethod.ServicePrincipal => "Service Principal (Client Credentials)",
            AuthMethod.UserAzureId => "User Azure ID (Interactive)",
            _ => "Unknown"
        };

        await TestTokenAcquisitionAsync(cancellationToken);
        await TestKeyVaultAsync(cancellationToken);
        await TestSqlAsync(cancellationToken);
        
        // Calculate overall health
        OverallHealthy = TokenAcquisitionOk && KeyVaultOk && SqlOk;
        OverallStatusMessage = OverallHealthy 
            ? "All connectivity checks passed successfully." 
            : $"{(new[] { TokenAcquisitionOk, KeyVaultOk, SqlOk }.Count(x => !x))} check(s) failed. Review error details below.";
    }

    private async Task TestTokenAcquisitionAsync(CancellationToken cancellationToken)
    {
        try
        {
            TokenCredential testCredential = SelectedAuthMethod switch
            {
                AuthMethod.ManagedIdentity => new ManagedIdentityCredential(),
                AuthMethod.ServicePrincipal => new ClientSecretCredential(
                    _config["Azure:ServicePrincipal:TenantId"],
                    _config["Azure:ServicePrincipal:ClientId"],
                    _config["Azure:ServicePrincipal:ClientSecret"]),
                AuthMethod.UserAzureId => new InteractiveBrowserCredential(),
                _ => throw new InvalidOperationException("Unknown authentication method")
            };

            var tokenRequestContext = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
            var token = await testCredential.GetTokenAsync(tokenRequestContext, cancellationToken);
            
            TokenAcquisitionValue = $"Token acquired successfully (expires: {token.ExpiresOn:yyyy-MM-dd HH:mm:ss UTC})";
            TokenAcquisitionMessage = $"{SelectedAuthMethod} successfully acquired ARM token";
            TokenAcquisitionOk = true;
            _logger.LogInformation("Successfully acquired token using {AuthMethod}", SelectedAuthMethod);
        }
        catch (Exception ex)
        {
            TokenAcquisitionOk = false;
            TokenAcquisitionError = ex.Message;
            TokenAcquisitionMessage = $"{SelectedAuthMethod} failed to acquire ARM token";
            _logger.LogError(ex, "Failed to acquire token using {AuthMethod}", SelectedAuthMethod);
        }
    }

    private async Task TestKeyVaultAsync(CancellationToken cancellationToken)
    {
        var secretName = _config["Azure:SqlConnectionStringSecretName"] ?? "AppSecretValue";
        SampleSecretName = secretName;
        
        try
        {
            var response = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            KeyVaultValue = response.Value.Value;
            KeyVaultMessage = $"Successfully retrieved secret '{secretName}'";
            KeyVaultOk = true;
        }
        catch (Exception ex)
        {
            KeyVaultOk = false;
            KeyVaultError = ex.Message;
            KeyVaultMessage = $"DNS resolution failed for Key Vault endpoint";
            _logger.LogError(ex, "Failed to read Key Vault secret {SecretName}", secretName);
        }
    }

    private async Task TestSqlAsync(CancellationToken cancellationToken)
    {
        var sqlConnSecretName = _config["Azure:SqlConnectionStringSecretName"] ?? "SqlConnectionString";
        try
        {
            var sqlConnSecret = await _secretClient.GetSecretAsync(sqlConnSecretName);
            var connStr = sqlConnSecret.Value.Value;

            await using var conn = new SqlConnection(connStr);
            
            // Acquire access token based on selected auth method
            TokenCredential sqlCredential = SelectedAuthMethod switch
            {
                AuthMethod.ManagedIdentity => new ManagedIdentityCredential(),
                AuthMethod.ServicePrincipal => new ClientSecretCredential(
                    _config["Azure:ServicePrincipal:TenantId"],
                    _config["Azure:ServicePrincipal:ClientId"],
                    _config["Azure:ServicePrincipal:ClientSecret"]),
                AuthMethod.UserAzureId => new InteractiveBrowserCredential(),
                _ => throw new InvalidOperationException("Unknown authentication method")
            };
            
            var token = await sqlCredential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://database.windows.net/.default" }), 
                cancellationToken);
            conn.AccessToken = token.Token;

            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP(1) SYSDATETIMEOFFSET();";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            SqlProbeResult = Convert.ToString(result) ?? "";
            SqlMessage = "Token-based SQL connection successful";
            SqlOk = true;
        }
        catch (Exception ex)
        {
            SqlOk = false;
            SqlError = ex.Message;
            SqlMessage = "Token-based SQL connection failed";
            _logger.LogError(ex, "Failed to connect to Azure SQL using {AuthMethod}", SelectedAuthMethod);
        }
    }
}

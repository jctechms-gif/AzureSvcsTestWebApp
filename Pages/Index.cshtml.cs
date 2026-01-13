
using System.Data;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace MiKvSqlRazor.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly IConfiguration _config;
    private readonly SecretClient _secretClient;
    private readonly TokenCredential _credential;

    public bool KeyVaultOk { get; private set; }
    public string? KeyVaultValue { get; private set; }
    public string? KeyVaultError { get; private set; }

    public bool SqlOk { get; private set; }
    public string? SqlError { get; private set; }
    public string? SqlProbeResult { get; private set; }

    // Add this property to your IndexModel class
    public string SampleSecretName { get; set; }

    public IndexModel(ILogger<IndexModel> logger, IConfiguration config, SecretClient secretClient, TokenCredential credential)
    {
        _logger = logger;
        _config = config;
        _secretClient = secretClient;
        _credential = credential;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await TestKeyVaultAsync(cancellationToken);
        await TestSqlAsync(cancellationToken);
    }

    private async Task TestKeyVaultAsync(CancellationToken cancellationToken)
    {
        var secretName = _config["Azure:SampleSecretName"] ?? "AppSecretValue";
        try
        {
            var response = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            KeyVaultValue = response.Value.Value;
            KeyVaultOk = true;
        }
        catch (Exception ex)
        {
            KeyVaultOk = false;
            KeyVaultError = ex.Message;
            _logger.LogError(ex, "Failed to read Key Vault secret {SecretName}", secretName);
        }
    }

    private async Task TestSqlAsync(CancellationToken cancellationToken)
    {
        var sqlConnSecretName = _config["Azure:SqlConnectionStringSecretName"] ?? "SqlConnectionString";
        try
        {
            var sqlConnSecret = await _secretClient.GetSecretAsync(sqlConnSecretName, cancellationToken: cancellationToken);
            var connStr = sqlConnSecret.Value.Value; // Should not contain user/password. We'll use AAD token.

            await using var conn = new SqlConnection(connStr);

            // Acquire an access token for Azure SQL and attach it to the connection
            // var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { "https://database.windows.net//.default" }), cancellationToken);
            // conn.AccessToken = token.Token;

            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP(1) SYSDATETIMEOFFSET();";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            SqlProbeResult = Convert.ToString(result);
            SqlOk = true;
        }
        catch (Exception ex)
        {
            SqlOk = false;
            SqlError = ex.Message;
            _logger.LogError(ex, "Failed to connect to Azure SQL using Managed Identity");
        }
    }
}

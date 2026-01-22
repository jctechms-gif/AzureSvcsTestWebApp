using Azure.Security.KeyVault.Secrets;

namespace SvcTestApp.Extensions;

/// <summary>
/// Extension methods for Azure Key Vault integration
/// </summary>
public static class KeyVaultExtensions
{
    /// <summary>
    /// Safely retrieves a secret from Key Vault with comprehensive error handling
    /// </summary>
    /// <param name="secretClient">The SecretClient instance</param>
    /// <param name="secretName">Name of the secret to retrieve</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>The secret value</returns>
    /// <exception cref="InvalidOperationException">Thrown when secret cannot be retrieved</exception>
    public static async Task<string> GetSecretSafelyAsync(this SecretClient secretClient, string secretName, ILogger logger)
    {
        try
        {
            logger.LogInformation("Attempting to retrieve secret '{SecretName}' from Key Vault", secretName);
            
            var secret = await secretClient.GetSecretAsync(secretName);
            
            if (string.IsNullOrEmpty(secret.Value.Value))
            {
                throw new InvalidOperationException($"Secret '{secretName}' exists but has no value");
            }
            
            logger.LogInformation("Successfully retrieved secret '{SecretName}' from Key Vault", secretName);
            return secret.Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogError("Secret '{SecretName}' not found in Key Vault. Status: {Status}, Error: {Error}", 
                secretName, ex.Status, ex.ErrorCode);
            throw new InvalidOperationException($"Secret '{secretName}' not found in Key Vault", ex);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403)
        {
            logger.LogError("Access denied to secret '{SecretName}'. Check Key Vault permissions. Status: {Status}, Error: {Error}", 
                secretName, ex.Status, ex.ErrorCode);
            throw new InvalidOperationException($"Access denied to secret '{secretName}'. Check Key Vault RBAC permissions or access policies.", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve secret '{SecretName}' from Key Vault: {Message}", 
                secretName, ex.Message);
            throw new InvalidOperationException($"Failed to retrieve secret '{secretName}' from Key Vault", ex);
        }
    }

    /// <summary>
    /// Retrieves Service Principal credentials from Key Vault
    /// </summary>
    /// <param name="secretClient">The SecretClient instance</param>
    /// <param name="tenantIdSecretName">Name of the tenant ID secret</param>
    /// <param name="clientIdSecretName">Name of the client ID secret</param>
    /// <param name="clientSecretSecretName">Name of the client secret</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>A tuple containing (tenantId, clientId, clientSecret)</returns>
    public static async Task<(string TenantId, string ClientId, string ClientSecret)> GetServicePrincipalCredentialsAsync(
        this SecretClient secretClient, 
        string tenantIdSecretName, 
        string clientIdSecretName, 
        string clientSecretSecretName, 
        ILogger logger)
    {
        try
        {
            logger.LogInformation("Retrieving Service Principal credentials from Key Vault");
            
            // Retrieve all three secrets concurrently for better performance
            var tenantTask = secretClient.GetSecretSafelyAsync(tenantIdSecretName, logger);
            var clientIdTask = secretClient.GetSecretSafelyAsync(clientIdSecretName, logger);
            var clientSecretTask = secretClient.GetSecretSafelyAsync(clientSecretSecretName, logger);
            
            await Task.WhenAll(tenantTask, clientIdTask, clientSecretTask);
            
            var tenantId = await tenantTask;
            var clientId = await clientIdTask;
            var clientSecret = await clientSecretTask;
            
            // Validate that all credentials are present
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("One or more Service Principal credential values are empty");
            }
            
            logger.LogInformation("Successfully retrieved all Service Principal credentials from Key Vault");
            return (tenantId, clientId, clientSecret);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve Service Principal credentials from Key Vault: {Message}", ex.Message);
            throw;
        }
    }
}
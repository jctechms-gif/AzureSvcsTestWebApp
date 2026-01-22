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
}
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvcTestApp.Data;
using SvcTestApp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Service Principal credentials will be retrieved from Key Vault
// Initialize with DefaultAzureCredential first to access Key Vault
builder.Services.AddSingleton<TokenCredential>(sp =>
{
    // This will be replaced with Service Principal credentials after Key Vault retrieval
    return new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    });
});

// Register SecretClient for Key Vault
builder.Services.AddSingleton<SecretClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var vaultUrl = config["Azure:KeyVaultUrl"] ?? throw new InvalidOperationException("Azure:KeyVaultUrl is required");
    var credential = sp.GetRequiredService<TokenCredential>();
    return new SecretClient(new Uri(vaultUrl), credential);
});

// Attempt to retrieve SQL connection string from Azure Key Vault
// If it fails, log the error but allow the application to start
var serviceProvider = builder.Services.BuildServiceProvider();
var secretClient = serviceProvider.GetRequiredService<SecretClient>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

string? connectionString = null;
bool keyVaultAvailable = false;

try
{
    logger.LogInformation("Retrieving Service Principal credentials from Key Vault");
    
    // Retrieve Service Principal credentials from Key Vault using extension method
    var tenantIdSecretName = builder.Configuration["Azure:SPTenantIDSecretName"] ?? "SPTenantId";
    var clientIdSecretName = builder.Configuration["Azure:SPClienttIDSecretName"] ?? "SPClientID";
    var clientSecretSecretName = builder.Configuration["Azure:SPClientSecretSecretName"] ?? "SPClientSecret";
    
    var (tenantId, clientId, clientSecret) = await secretClient.GetServicePrincipalCredentialsAsync(
        tenantIdSecretName, clientIdSecretName, clientSecretSecretName, logger);
    
    // Create new Service Principal credential
    var servicePrincipalCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    
    // Update the TokenCredential service registration
    builder.Services.RemoveAll<TokenCredential>();
    builder.Services.AddSingleton<TokenCredential>(servicePrincipalCredential);
    
    // Create new SecretClient with Service Principal credentials
    var vaultUrl = builder.Configuration["Azure:KeyVaultUrl"] ?? throw new InvalidOperationException("Azure:KeyVaultUrl is required");
    var newSecretClient = new SecretClient(new Uri(vaultUrl), servicePrincipalCredential);
    
    // Update the SecretClient service registration
    builder.Services.RemoveAll<SecretClient>();
    builder.Services.AddSingleton(newSecretClient);
    
    logger.LogInformation("Successfully configured Service Principal authentication from Key Vault");
    
    // Now retrieve the SQL connection string using Service Principal credentials
    var sqlSecretName = builder.Configuration["Azure:SqlConnectionStringSecretName"] ?? "SqlConnectionString";
    logger.LogInformation("Retrieving connection string from Key Vault secret: {SecretName}", sqlSecretName);
    
    connectionString = await newSecretClient.GetSecretSafelyAsync(sqlSecretName, logger);
    
    // Add the connection string to configuration
    builder.Configuration["ConnectionStrings:Default"] = connectionString;
    keyVaultAvailable = true;
    
    logger.LogInformation("Successfully configured connection string from Key Vault using Service Principal");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to retrieve Service Principal credentials or connection string from Key Vault: {Message}. Application will start with default credentials and without database connectivity.", ex.Message);
    
    // Use a fallback connection string (won't work, but allows app to start)
    // You can also use a local connection string from appsettings.json if available
    connectionString = builder.Configuration.GetConnectionString("Default") 
                      ?? "Server=(unavailable);Database=(unavailable);";
    
    builder.Configuration["ConnectionStrings:Default"] = connectionString;
}

// Store Key Vault availability status and auth method for health checks
builder.Services.AddSingleton(new KeyVaultHealthStatus { IsAvailable = keyVaultAvailable });
builder.Services.AddSingleton(new AuthenticationStatus 
{ 
    Method = keyVaultAvailable ? "ServicePrincipal" : "DefaultAzureCredential",
    IsConfiguredFromKeyVault = keyVaultAvailable 
});

// Register EF Core with SQL Server provider (uses Microsoft.Data.SqlClient internally)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connStr = builder.Configuration.GetConnectionString("Default");
    options.UseSqlServer(connStr);
    
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

// Add health checks with graceful failure handling
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", 
        failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
        tags: new[] { "db", "sql" })
    .AddCheck("keyvault", () =>
    {
        var kvStatus = serviceProvider.GetRequiredService<KeyVaultHealthStatus>();
        return kvStatus.IsAvailable
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Key Vault is accessible")
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Key Vault is not accessible");
    }, tags: new[] { "keyvault" })
    .AddCheck("authentication", () =>
    {
        var authStatus = serviceProvider.GetRequiredService<AuthenticationStatus>();
        var result = authStatus.IsConfiguredFromKeyVault
            ? $"Authentication configured using {authStatus.Method} from Key Vault"
            : $"Authentication using fallback {authStatus.Method}";
        
        return authStatus.IsConfiguredFromKeyVault
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(result)
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded(result);
    }, tags: new[] { "auth" });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Map both Razor Pages and API controllers
app.MapRazorPages();
app.MapControllers(); // Enable API controller routing
app.MapHealthChecks("/health"); // Add health check endpoint

app.Run();

// Helper classes to track Key Vault and authentication status
public class KeyVaultHealthStatus
{
    public bool IsAvailable { get; set; }
}

public class AuthenticationStatus
{
    public string Method { get; set; } = "";
    public bool IsConfiguredFromKeyVault { get; set; }
}
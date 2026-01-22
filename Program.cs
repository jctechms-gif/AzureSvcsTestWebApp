using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.EntityFrameworkCore;
using SvcTestApp.Data;
using SvcTestApp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Note: With the toggle feature, credentials are now created dynamically in IndexModel
// You can keep this for other services that need a default credential
builder.Services.AddSingleton<TokenCredential>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var defaultMethod = config["Azure:DefaultAuthMethod"];
    
    return defaultMethod switch
    {
        "ServicePrincipal" => new ClientSecretCredential(
            config["Azure:ServicePrincipal:TenantId"],
            config["Azure:ServicePrincipal:ClientId"],
            config["Azure:ServicePrincipal:ClientSecret"]),
        _ => new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        })
    };
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
    var secretName = builder.Configuration["Azure:SqlConnectionStringSecretName"] ?? "Sql--ConnectionString";
    logger.LogInformation("Retrieving connection string from Key Vault secret: {SecretName}", secretName);
    
    // Use the safer extension method for Key Vault access
    connectionString = await secretClient.GetSecretSafelyAsync(secretName, logger);
    
    // Add the connection string to configuration
    builder.Configuration["ConnectionStrings:Default"] = connectionString;
    keyVaultAvailable = true;
    
    logger.LogInformation("Successfully configured connection string from Key Vault");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to retrieve connection string from Key Vault: {Message}. Application will start without database connectivity.", ex.Message);
    
    // Use a fallback connection string (won't work, but allows app to start)
    // You can also use a local connection string from appsettings.json if available
    connectionString = builder.Configuration.GetConnectionString("Default") 
                      ?? "Server=(unavailable);Database=(unavailable);";
    
    builder.Configuration["ConnectionStrings:Default"] = connectionString;
}

// Store Key Vault availability status for health checks
builder.Services.AddSingleton(new KeyVaultHealthStatus { IsAvailable = keyVaultAvailable });

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
    }, tags: new[] { "keyvault" });

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

// Helper class to track Key Vault availability
public class KeyVaultHealthStatus
{
    public bool IsAvailable { get; set; }
}
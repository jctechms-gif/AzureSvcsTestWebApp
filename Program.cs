using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
var azureSection = builder.Configuration.GetSection("Azure");

builder.Services.AddRazorPages();

// Register DefaultAzureCredential (works locally and in Azure)
builder.Services.AddSingleton<TokenCredential>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var clientId = config["Azure:ManagedIdentityClientId"]; // optional for user-assigned MI
    var options = new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId,
        ExcludeInteractiveBrowserCredential = true
    };
    return new DefaultAzureCredential(options);
});

// Register SecretClient for Key Vault
builder.Services.AddSingleton<SecretClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var vaultUrl = config["Azure:KeyVaultUrl"] ?? throw new InvalidOperationException("Azure:KeyVaultUrl is required");
    var credential = sp.GetRequiredService<TokenCredential>();
    return new SecretClient(new Uri(vaultUrl), credential);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();
# AzureSvcsTestWebApp

An ASP.NET Core web application that demonstrates authentication with Azure SQL Database using Service Principal credentials from Azure Key Vault and Entity Framework Core.

## Architecture Overview

This application uses:
- **Entity Framework Core** with SQL Server provider
- **Azure Key Vault** for secure credential and connection string storage
- **Service Principal authentication** with Microsoft Entra ID for database access
- **Microsoft.Data.SqlClient** for SQL Server connectivity
- **ClientSecretCredential** for Service Principal authentication

## Authentication Flow

1. **Bootstrap**: Application starts with `DefaultAzureCredential` to access Key Vault
2. **Retrieve Credentials**: Gets Service Principal credentials (Tenant ID, Client ID, Client Secret) from Key Vault
3. **Create Authentication**: Creates `ClientSecretCredential` using Service Principal details
4. **Database Connection**: Uses Service Principal to authenticate with SQL Database via connection string

## Prerequisites

### 1. Azure Key Vault Setup
Required secrets in Key Vault:
- `SPTenantId` - Service Principal Tenant ID (configurable via `Azure:SPTenantIDSecretName`)
- `SPClientID` - Service Principal Client ID (configurable via `Azure:SPClienttIDSecretName`) 
- `SPClientSecret` - Service Principal Client Secret (configurable via `Azure:SPClientSecretSecretName`)
- `SqlConnectionString` - Database connection string (configurable via `Azure:SqlConnectionStringSecretName`)

### 2. Service Principal Setup
1. **Create Service Principal**:
   ```bash
   az ad sp create-for-rbac --name "YourAppServicePrincipal" --role contributor --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group}
   ```
   
2. **Store credentials in Key Vault**:
   ```bash
   az keyvault secret set --vault-name "your-keyvault" --name "SPTenantId" --value "{tenant-id}"
   az keyvault secret set --vault-name "your-keyvault" --name "SPClientID" --value "{client-id}"
   az keyvault secret set --vault-name "your-keyvault" --name "SPClientSecret" --value "{client-secret}"
   ```

### 3. SQL Server Setup
1. **Set Entra ID admin** on the SQL server:
   ```bash
   az sql server ad-admin create --resource-group <rg> --server-name <server> --display-name <admin-name> --object-id <admin-object-id>
   ```

2. **Create contained user** for the Service Principal:
   ```sql
   -- Connect to the database using Entra ID admin
   CREATE USER [YourAppServicePrincipal] FROM EXTERNAL PROVIDER;
   
   -- Grant minimal required permissions
   ALTER ROLE db_datareader ADD MEMBER [YourAppServicePrincipal];
   ALTER ROLE db_datawriter ADD MEMBER [YourAppServicePrincipal];
   
   -- Optional: If running migrations in production
   ALTER ROLE db_ddladmin ADD MEMBER [YourAppServicePrincipal];
   ```

3. **Connection string format**:
   ```
   Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Service Principal;
   ```

### 4. Access Permissions
The bootstrap identity (local dev user or managed identity) needs:
- **Key Vault**: `Key Vault Secrets User` role or access policy for "Get" secret permissions on the credential secrets

## Configuration

### Environment Variables
- `Azure__KeyVaultUrl`: URL to your Azure Key Vault (e.g., `https://your-vault.vault.azure.net/`)
- `Azure__SqlConnectionStringSecretName`: Name of the Key Vault secret containing the connection string (default: `Sql--ConnectionString`)

### appsettings.json
```json
{
  "Azure": {
    "KeyVaultUrl": "https://your-vault.vault.azure.net/",
    "SqlConnectionStringSecretName": "Sql--ConnectionString"
  }
}
```

## Authentication Flow

### Local Development
`DefaultAzureCredential` resolves credentials in this order:
1. **EnvironmentCredential** - Service principal from environment variables
2. **WorkloadIdentityCredential** - Azure Kubernetes workload identity
3. **ManagedIdentityCredential** - System/user-assigned managed identity (unused locally)
4. **SharedTokenCacheCredential** - Shared token cache (Azure CLI/Visual Studio)
5. **VisualStudioCredential** - Visual Studio authentication
6. **VisualStudioCodeCredential** - VS Code Azure Account extension
7. **AzureCliCredential** - Azure CLI authentication
8. **AzurePowerShellCredential** - Azure PowerShell authentication
9. **InteractiveBrowserCredential** - Disabled for this app

To authenticate locally:
```bash
# Using Azure CLI (recommended)
az login

# Using Azure PowerShell
Connect-AzAccount
```

### Azure Deployment
When deployed to Azure, `DefaultAzureCredential` automatically uses the **Managed Identity** assigned to the resource:
- App Service: System-assigned or user-assigned managed identity
- Azure Container Instances: User-assigned managed identity
- Azure Functions: System-assigned or user-assigned managed identity

## Database Connectivity

### Database Ping Endpoint
- **GET** `/db-ping` - Tests database connectivity
- Returns `200 {"status":"ok"}` on success
- Returns `503` with error details on failure

### EF Core Setup
The application automatically:
1. Retrieves the connection string from Azure Key Vault at startup
2. Configures EF Core with SQL Server provider
3. Uses Microsoft.Data.SqlClient internally for all database operations

### Migrations
To create and apply migrations:
```bash
# Install EF Core tools (one-time)
dotnet tool install --global dotnet-ef

# Create a new migration
dotnet ef migrations add <MigrationName>

# Apply migrations to database
dotnet ef database update
```

**Note**: In production, avoid auto-applying migrations. Apply them manually or via deployment pipeline.

## Troubleshooting

### Key Vault Access Issues
1. **403 Forbidden**: Check RBAC permissions or access policies
   - Ensure the identity has `Key Vault Secrets User` role
   - For access policies, grant "Get" secret permission
2. **Identity not found**: Verify the managed identity is created and assigned
3. **Vault not found**: Check the `Azure:KeyVaultUrl` configuration

### SQL Permission Errors
1. **Login failed for user**: 
   - Ensure Entra ID admin is set on SQL server
   - Verify the contained user is created in the target database
2. **Permission denied**:
   - Check database role memberships for the application identity
   - Ensure minimal roles are granted: `db_datareader`, `db_datawriter`
3. **Cannot open database**:
   - Verify the database name in the connection string
   - Check that the user has access to the specific database

### Local Development Issues
1. **No credentials available**:
   ```bash
   # Sign in with Azure CLI
   az login
   
   # Verify account
   az account show
   ```
2. **Wrong subscription**: 
   ```bash
   az account set --subscription <subscription-id>
   ```

### Network Issues
1. **Firewall rules**: Ensure the SQL server allows connections from your IP or Azure services
2. **Virtual network**: If using VNet integration, check network security group rules

## Security Best Practices

1. **No passwords in code**: Connection strings use `Authentication=Active Directory Default`
2. **Minimal permissions**: Database users have only required roles
3. **Key Vault secrets**: Connection strings are stored securely, not in configuration files
4. **TLS encryption**: All connections use `Encrypt=True`
5. **Credential rotation**: Managed identities are automatically rotated by Azure

## Development Workflow

1. **Local setup**:
   ```bash
   git clone <repository>
   cd AzureSvcsTestWebApp
   az login
   dotnet restore
   dotnet run
   ```

2. **Test connectivity**:
   ```bash
   curl https://localhost:5001/db-ping
   ```

3. **Deploy to Azure**:
   - Ensure managed identity is assigned
   - Configure Key Vault access
   - Set up SQL database permissions
   - Deploy application
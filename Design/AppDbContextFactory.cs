using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SvcTestApp.Data;

namespace SvcTestApp.Design;

/// <summary>
/// Design-time factory for AppDbContext to support EF Core tooling
/// This is used during migrations and other design-time operations
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        // Use a temporary connection string for design-time operations
        // This won't be used at runtime - the real connection string comes from Key Vault
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=SvcTestApp;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=false"
        );
        
        return new AppDbContext(optionsBuilder.Options);
    }
}
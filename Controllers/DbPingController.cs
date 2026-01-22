using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SvcTestApp.Data;
using System.Data;

namespace SvcTestApp.Controllers;

/// <summary>
/// Controller for database connectivity probes
/// </summary>
[ApiController]
[Route("[controller]")]
public class DbPingController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<DbPingController> _logger;

    public DbPingController(AppDbContext context, ILogger<DbPingController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Database connectivity probe endpoint
    /// Tests database connectivity via EF Core
    /// </summary>
    /// <returns>Status of database connection</returns>
    [HttpGet]
    [Route("/db-ping")]
    public async Task<IActionResult> Ping()
    {
        try
        {
            _logger.LogInformation("Database connectivity probe started");

            // Open a connection via EF Core and run SELECT 1
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandType = CommandType.Text;
            
            var result = await command.ExecuteScalarAsync();
            
            if (result != null && result.ToString() == "1")
            {
                _logger.LogInformation("Database connectivity probe succeeded");
                return Ok(new { status = "ok" });
            }
            else
            {
                _logger.LogWarning("Database connectivity probe returned unexpected result: {Result}", result);
                return StatusCode(503, new 
                { 
                    status = "error", 
                    message = "Database returned unexpected result",
                    result = result?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connectivity probe failed: {Message}", ex.Message);
            return StatusCode(503, new 
            { 
                status = "error", 
                message = ex.Message,
                type = ex.GetType().Name
            });
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace PDFOrganizerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseTestController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseTestController> _logger;

        public DatabaseTestController(
            IConfiguration configuration,
            ILogger<DatabaseTestController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("CheckConnection")]
        public async Task<IActionResult> CheckConnection()
        {
            string? connectionString = _configuration
                .GetConnectionString("appdbConnect");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return StatusCode(503, new
                {
                    Status = "Failed",
                    Message = "Database configuration missing"
                });
            }

            try
            {
                var builder =
                    new SqlConnectionStringBuilder(connectionString)
                    {
                        ConnectTimeout = 5
                    };

                await using var connection =
                    new SqlConnection(builder.ConnectionString);

                await connection.OpenAsync();

                await using var command =
                    new SqlCommand("SELECT 1", connection);

                await command.ExecuteScalarAsync();

                return Ok(new
                {
                    Status = "Success",
                    Message = "Render API connected to SQL Server"
                });
            }
            catch (SqlException ex)
            {
                _logger.LogError(
                    ex,
                    "SQL Server connection test failed");

                return StatusCode(503, new
                {
                    Status = "Failed",
                    Message = "SQL Server connection failed. Check Render logs."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected database test failure");

                return StatusCode(500, new
                {
                    Status = "Failed",
                    Message = "Database test failed. Check Render logs."
                });
            }
        }
    }
}  

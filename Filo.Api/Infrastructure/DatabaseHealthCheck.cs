using Filo.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Filo.Api.Infrastructure
{
    /// <summary>
    /// Reports whether the API can reach PostgreSQL.
    /// </summary>
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly FiloDbContext _context;

        public DatabaseHealthCheck(FiloDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _context.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Database unavailable.");
            }
            catch (Exception ex)
            {
                // The response only shows the status; details go to the log.
                return HealthCheckResult.Unhealthy("Database unavailable.", ex);
            }
        }
    }
}

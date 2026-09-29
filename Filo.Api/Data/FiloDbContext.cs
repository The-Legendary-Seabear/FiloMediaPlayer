using Microsoft.EntityFrameworkCore;

namespace Filo.Api.Data
{
    public class FiloDbContext : DbContext
    {
        public FiloDbContext(DbContextOptions<FiloDbContext> options)
            : base(options)
        {
        }
    }
}
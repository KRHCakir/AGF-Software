using AgfAdmin.Data;

namespace AgfAdmin.Services
{
    public interface IDevelopmentDataSeeder
    {
        Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default);
    }
}

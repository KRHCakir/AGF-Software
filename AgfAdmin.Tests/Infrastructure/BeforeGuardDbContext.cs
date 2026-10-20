using AgfAdmin.Data;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// Deterministische race vóór de guard: de eerste SaveChangesAsync voert eerst een gelijktijdige schrijver volledig uit (eigen context,
// eigen commit) en gaat daarna pas door naar AppDbContext.SaveChangesAsync, dus naar de TraceabilityGuard en de database. De service
// heeft op dat moment al zijn controles gedaan. Anders dan een EF-interceptor (die pas ná de guard draait) raakt dit precies het
// venster tussen servicecontrole en guard.
public sealed class BeforeGuardDbContext : AppDbContext
{
    private Func<Task> _beforeGuard;

    public BeforeGuardDbContext(DbContextOptions<AppDbContext> options, IBusinessCalendar calendar, Func<Task> beforeGuard) : base(options, calendar)
    {
        _beforeGuard = beforeGuard;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var concurrent = _beforeGuard;
        _beforeGuard = null;
        if (concurrent != null)
        {
            await concurrent();
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

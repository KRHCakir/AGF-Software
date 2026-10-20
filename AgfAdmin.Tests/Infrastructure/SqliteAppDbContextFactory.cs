using AgfAdmin.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Infrastructure;

// Echte relationele testdatabase (SQLite in-memory) zodat FK's, unieke indexen en
// delete-gedrag door de database zelf worden afgedwongen, anders dan bij EF InMemory.
// Alleen voor tests: het schema wordt hier met EnsureCreated opgebouwd, nooit in de applicatie.
public sealed class SqliteAppDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteAppDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    // Kalender voor de datumcontroles in de guard: vast op 20 oktober 2026 (Europe/Amsterdam), zodat tests niet van de systeemklok afhangen.
    // Tests die de tijdzone zelf toetsen geven hun eigen kalender mee, en dezelfde aan de service.
    public IBusinessCalendar Calendar { get; set; } = new FixedBusinessCalendar(new DateOnly(2026, 10, 20));

    public AppDbContext CreateContext() => new(_options, Calendar);

    // Voor deterministische concurrency-tests: een interceptor kan midden in SaveChanges een gelijktijdige wijziging doen.
    public AppDbContext CreateContext(params IInterceptor[] interceptors) => CreateContext(Calendar, interceptors);

    public AppDbContext CreateContext(IBusinessCalendar calendar, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options, calendar ?? Calendar);

    // Context waarvan de eerste SaveChangesAsync eerst `beforeGuard` volledig uitvoert en pas daarna de guard en de database bereikt.
    public AppDbContext CreateContextBeforeGuard(Func<Task> beforeGuard) => new BeforeGuardDbContext(_options, Calendar, beforeGuard);

    public void Dispose() => _connection.Dispose();
}
